using Ideax.Data;
using Ideax.Entities;
using Ideax.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace Ideax.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly Ideax.Services.Payments.PaystackApiClient _paystack;
    private readonly Ideax.Services.Email.IEmailSender _emailSender;
    private readonly IConfiguration _config;

    public PaymentsController(ApplicationDbContext db, Ideax.Services.Payments.PaystackApiClient paystack, Ideax.Services.Email.IEmailSender emailSender, IConfiguration config)
    {
        _db = db;
        _paystack = paystack;
        _emailSender = emailSender;
        _config = config;
    }

    [Authorize]
    [HttpPost("initiatialize")]
    public async Task<IActionResult> Initiate([FromBody] InitiatePaymentDto dto)
    {
        var req = await _db.ServiceRequests.Include(r => r.Service).Include(r => r.User).FirstOrDefaultAsync(r => r.Id == dto.ServiceRequestId);
        if (req == null) return NotFound(Ideax.DTOs.ApiResponse.Fail("Service request not found."));
        if (req.Status != RequestStatus.PendingPayment) return BadRequest(Ideax.DTOs.ApiResponse.Fail("Payment already initiated or completed."));

        var amount = req.Service?.Price ?? 0m;

        // Determine callback/return URL: prefer configured Paystack:CallbackUrl, fallback to dto.ReturnUrl
        var callback = _config?["Paystack:CallbackUrl"];
        if (string.IsNullOrWhiteSpace(callback)) callback = dto.ReturnUrl;
        if (string.IsNullOrWhiteSpace(callback)) return BadRequest(Ideax.DTOs.ApiResponse.Fail("Callback URL not configured."));

        // call provider to create payment session
        var userEmail = req.User?.Email ?? string.Empty;
        string paymentUrl;
        string externalId;
        try
        {
            (paymentUrl, externalId) = await _paystack.CreatePaymentSessionAsync(amount, callback, userEmail);
        }
        catch (Exception ex)
        {
            return StatusCode(502, Ideax.DTOs.ApiResponse.Fail("Failed to initialize payment with provider.", new[] { ex.Message }));
        }

        // create local payment record referencing provider reference
        var payment = new Payment
        {
            ServiceRequestId = req.Id,
            Amount = amount,
            TransactionId = externalId,
            Status = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        // return the payment url to the client
        return Ok(Ideax.DTOs.ApiResponse.Ok(new { paymentUrl, paymentId = payment.Id }, "Payment initiated"));
    }

    // Paystack webhook endpoint. Verifies signature and processes events.
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var secret = _config?["Paystack:SecretKey"] ?? string.Empty;

        // Read raw body
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        // Verify signature header
        if (!Request.Headers.TryGetValue("x-paystack-signature", out var sigHeader))
        {
            return BadRequest();
        }

        var computed = ComputeHmacSha512(secret, body);
        if (!string.Equals(computed, sigHeader.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized();
        }

        // Parse payload
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var eventName = root.GetProperty("event").GetString();
        var data = root.GetProperty("data");
        var reference = data.GetProperty("reference").GetString();
        var status = data.GetProperty("status").GetString();
        var amountInKobo = data.GetProperty("amount").GetInt64();
        var amount = amountInKobo / 100m;

        var payment = await _db.Payments.Include(p => p.ServiceRequest).ThenInclude(r => r.User).FirstOrDefaultAsync(p => p.TransactionId == reference);
        if (payment == null)
        {
            // Unknown payment -- ignore
            return Ok();
        }

        if (eventName == "charge.success" || string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Completed;
            payment.CompletedAt = DateTime.UtcNow;
            payment.Amount = amount;
            if (payment.ServiceRequest != null)
            {
                payment.ServiceRequest.Status = RequestStatus.Paid;
                payment.ServiceRequest.PaidAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            try
            {
                var user = payment.ServiceRequest?.User;
                if (user != null)
                {
                    var subject = "Payment received";
                    var html = $"<p>Hi {user.FullName},</p><p>Your payment of {payment.Amount:C} was received. We will start your service shortly.</p>";
                    await _emailSender.SendEmailAsync(user.Email!, subject, html);
                }
            }
            catch { }
        }
        else
        {
            payment.Status = PaymentStatus.Failed;
            await _db.SaveChangesAsync();
        }

        return Ok();
    }

    [Authorize]
    [HttpGet("verify")]
    public async Task<IActionResult> Verify([FromQuery] string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return BadRequest(Ideax.DTOs.ApiResponse.Fail("reference required"));

        var payment = await _db.Payments.Include(p => p.ServiceRequest).ThenInclude(r => r.User).FirstOrDefaultAsync(p => p.TransactionId == reference);
        if (payment == null) return NotFound(Ideax.DTOs.ApiResponse.Fail("Payment not found."));

        var (success, extId, amount) = await _paystack.VerifyPaymentAsync(reference);
        if (!success) return BadRequest(Ideax.DTOs.ApiResponse.Fail("Payment verification failed"));

        payment.Status = PaymentStatus.Completed;
        payment.CompletedAt = DateTime.UtcNow;
        payment.Amount = amount;
        if (payment.ServiceRequest != null)
        {
            payment.ServiceRequest.Status = RequestStatus.Paid;
            payment.ServiceRequest.PaidAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();

        try
        {
            var user = payment.ServiceRequest?.User;
            if (user != null)
            {
                var subject = "Payment received";
                var html = $"<p>Hi {user.FullName},</p><p>Your payment of {payment.Amount:C} was received. We will start your service shortly.</p>";
                await _emailSender.SendEmailAsync(user.Email!, subject, html);
            }
        }
        catch { }

        return Ok(Ideax.DTOs.ApiResponse.Ok(new { message = "Payment verified and recorded." }));
    }

    private static string ComputeHmacSha512(string key, string payload)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key ?? string.Empty);
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty);
        using var hmac = new HMACSHA512(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

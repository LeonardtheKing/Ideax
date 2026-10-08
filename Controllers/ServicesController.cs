using Ideax.Data;
using Ideax.Entities;
using Ideax.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace Ideax.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly Ideax.Services.Email.IEmailSender _emailSender;
    private readonly UserManager<User> _userManager;

    public ServicesController(ApplicationDbContext db, Ideax.Services.Email.IEmailSender emailSender, UserManager<User> userManager)
    {
        _db = db;
        _emailSender = emailSender;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var services = await _db.Services.Where(s => s.IsActive).ToListAsync();
        return Ok(Ideax.DTOs.ApiResponse.Ok(services));
    }

    [Authorize]
    [HttpPost("{serviceId:guid}/select")]
    public async Task<IActionResult> SelectService(Guid serviceId, [FromBody] CreateServiceRequestDto model)
    {
        var svc = await _db.Services.FindAsync(serviceId);
        if (svc == null) return NotFound(Ideax.DTOs.ApiResponse.Fail("Service not found."));

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized(Ideax.DTOs.ApiResponse.Fail("Unauthorized"));

        var userId = Guid.Parse(userIdClaim);

        var req = new ServiceRequest
        {
            ServiceId = svc.Id,
            UserId = userId,
            Details = model.Details,
            Status = RequestStatus.PendingPayment
        };

        _db.ServiceRequests.Add(req);
        await _db.SaveChangesAsync();

        // send email to user confirming selection
        try
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user != null)
            {
                var subject = $"Service selected: {svc.Name}";
                var html = $"<p>Hi {user.FullName},</p><p>You selected the service <strong>{svc.Name}</strong>.</p><p>Details: {req.Details}</p>";
                await _emailSender.SendEmailAsync(user.Email!, subject, html);
            }
        }
        catch { }

        return Ok(Ideax.DTOs.ApiResponse.Ok(req, "Service request created."));
    }
}

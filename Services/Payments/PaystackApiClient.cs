using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;

namespace Ideax.Services.Payments;

public class PaystackApiClient
{
    private readonly HttpClient _http;
    private readonly string _secretKey;

    public PaystackApiClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _secretKey = config["Paystack:SecretKey"] ?? string.Empty;
        var baseUrl = config["Paystack:BaseUrl"] ?? "https://api.paystack.co/";
        if (_http.BaseAddress == null) _http.BaseAddress = new Uri(baseUrl);
    }

    public async Task<(string PaymentUrl, string ExternalPaymentId)> CreatePaymentSessionAsync(decimal amount, string returnUrl, string email)
    {
        // Paystack expects amount in kobo (smallest currency unit)
        var amountInKobo = Convert.ToInt64(amount * 100);

        var payload = new
        {
            email = email,
            amount = amountInKobo,
            callback_url = returnUrl
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "transaction/initialize");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var res = await _http.SendAsync(req);
        var content = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        if (!res.IsSuccessStatusCode)
        {
            var msg = doc.RootElement.GetProperty("message").GetString() ?? "Paystack initialize failed";
            throw new InvalidOperationException(msg);
        }

        var data = doc.RootElement.GetProperty("data");
        var authUrl = data.GetProperty("authorization_url").GetString()!;
        var reference = data.GetProperty("reference").GetString()!;

        return (authUrl, reference);
    }

    public async Task<(bool Success, string ExternalPaymentId, decimal Amount)> VerifyPaymentAsync(string reference)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"transaction/verify/{reference}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);

        using var res = await _http.SendAsync(req);
        var content = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        if (!res.IsSuccessStatusCode)
        {
            return (false, reference, 0m);
        }

        var data = doc.RootElement.GetProperty("data");
        var status = data.GetProperty("status").GetString();
        var amountInKobo = data.GetProperty("amount").GetInt64();
        var amount = amountInKobo / 100m;

        return (status == "success", reference, amount);
    }
}

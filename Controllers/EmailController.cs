using Ideax.DTOs;
using Ideax.Services.Email;
using Microsoft.AspNetCore.Mvc;

namespace Ideax.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailController : ControllerBase
{
    private readonly IEmailSender _emailSender;

    public EmailController(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    [HttpPost("test")]
    public async Task<IActionResult> TestEmail([FromBody] TestEmailDto dto)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(Ideax.DTOs.ApiResponse.Fail("Validation failed", errors));
        }

        var subject = dto.Subject ?? "Idea X - Test Email";
        var body = dto.Body ?? "<p>This is a test email from Idea X.</p>";

        try
        {
            await _emailSender.SendEmailAsync(dto.To, subject, body);
            return Ok(Ideax.DTOs.ApiResponse.Ok(null, "Email sent (or queued)"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, Ideax.DTOs.ApiResponse.Fail(ex.Message));
        }
    }
}

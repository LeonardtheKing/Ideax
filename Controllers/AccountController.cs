using Ideax.DTOs;
using Ideax.Entities;
using Ideax.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ideax.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccountController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ApplicationDbContext _db;
    private readonly Ideax.Services.Email.IEmailSender _emailSender;
    private readonly Ideax.Services.Token.ITokenService _tokenService;

    public AccountController(UserManager<User> userManager,
        SignInManager<User> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ApplicationDbContext db,
        Ideax.Services.Email.IEmailSender emailSender,
        Ideax.Services.Token.ITokenService tokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _db = db;
        _emailSender = emailSender;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(Ideax.DTOs.ApiResponse.Fail("Validation failed", errors));
        }

        if (model.Password != model.PasswordConfirmation)
            return BadRequest(Ideax.DTOs.ApiResponse.Fail("Password and confirmation do not match."));

        var user = new User
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            Role = Ideax.Entities.UserRole.Client
        };

        // Use a database transaction to ensure operations are atomic
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var createResult = await _userManager.CreateAsync(user, model.Password);
                if (!createResult.Succeeded)
                {
                    await tx.RollbackAsync();
                    var errs = createResult.Errors.Select(e => e.Description);
                    return BadRequest(Ideax.DTOs.ApiResponse.Fail("Failed to create user", errs));
                }

            // Ensure role exists and assign
            var roleName = user.Role.ToString();
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var role = new IdentityRole<Guid>(roleName);
                var rm = await _roleManager.CreateAsync(role);
                if (!rm.Succeeded)
                {
                    await tx.RollbackAsync();
                    return StatusCode(500, Ideax.DTOs.ApiResponse.Fail("Failed to create role."));
                }
            }

            var addRoleResult = await _userManager.AddToRoleAsync(user, roleName);
            if (!addRoleResult.Succeeded)
            {
                await tx.RollbackAsync();
                var errs = addRoleResult.Errors.Select(e => e.Description);
                return StatusCode(500, Ideax.DTOs.ApiResponse.Fail("Failed to assign role", errs));
            }

            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, Ideax.DTOs.ApiResponse.Fail(ex.Message));
        }

        // Send welcome email (do not roll back registration if email fails)
        try
        {
            var subject = "Welcome to Idea X";
            var html = $"<p>Hi {user.FullName},</p><p>Your account was created successfully.</p>";
            await _emailSender.SendEmailAsync(user.Email, subject, html);
        }
        catch
        {
            // Log is handled inside the email sender; ignore failure here
        }

        var responseData = new
        {
            user.FullName,
            user.Email
        };

        return Ok(Ideax.DTOs.ApiResponse.Ok(responseData, "Registration successful."));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(Ideax.DTOs.ApiResponse.Fail("Validation failed", errors));
        }

        var user = await _userManager.Users.SingleOrDefaultAsync(u => u.Email == model.Email);
        if (user == null) return Unauthorized(Ideax.DTOs.ApiResponse.Fail("Invalid credentials."));

        var pwOk = await _userManager.CheckPasswordAsync(user, model.Password);
        if (!pwOk) return Unauthorized(Ideax.DTOs.ApiResponse.Fail("Invalid credentials."));

        var roles = await _userManager.GetRolesAsync(user);
        var token = _tokenService.CreateToken(user, roles);

        // parse expiration from token? We can compute from JwtSettings, but return approximate
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        var response = new LoginResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt,
            UserId = user.Id.ToString(),
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Roles = roles
        };

        return Ok(Ideax.DTOs.ApiResponse.Ok(response));
    }
}

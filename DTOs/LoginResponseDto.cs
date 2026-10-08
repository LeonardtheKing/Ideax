namespace Ideax.DTOs;

public class LoginResponseDto
{
    public required string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public required string UserId { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    public IEnumerable<string> Roles { get; set; } = Array.Empty<string>();
}

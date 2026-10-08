namespace Ideax.Services.Email;

public class EmailOptions
{
    public required string Host { get; set; }
    public int Port { get; set; } = 25;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public required string From { get; set; }
    public bool EnableSsl { get; set; } = true;
}

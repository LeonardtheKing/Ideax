namespace Ideax.DTOs;

public class TestEmailDto
{
    public required string To { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

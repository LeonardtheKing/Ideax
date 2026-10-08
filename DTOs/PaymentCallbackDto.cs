namespace Ideax.DTOs;

public class PaymentCallbackDto
{
    public required Guid PaymentId { get; set; }
    public string? ExternalPaymentId { get; set; }
    public required string Status { get; set; }
}

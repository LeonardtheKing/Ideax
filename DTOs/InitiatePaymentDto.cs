namespace Ideax.DTOs;

public class InitiatePaymentDto
{
    public required Guid ServiceRequestId { get; set; }
    public required string ReturnUrl { get; set; }
}

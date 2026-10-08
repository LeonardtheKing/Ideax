using System;

namespace Ideax.Entities;

public class Payment
{
    public Guid Id { get; set; }

    public Guid ServiceRequestId { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }

    public decimal Amount { get; set; }
    public string? TransactionId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

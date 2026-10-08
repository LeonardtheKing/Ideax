using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ideax.Entities;

public class ServiceRequest
{
    public Guid Id { get; set; }

    // Who requested the service
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // Which service was requested
    public Guid ServiceId { get; set; }
    public Service? Service { get; set; }

    // Details provided by the user describing what they want
    [Required]
    public required string Details { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.PendingPayment;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }

    // Navigation to payment (may be null until paid)
    public Payment? Payment { get; set; }
}

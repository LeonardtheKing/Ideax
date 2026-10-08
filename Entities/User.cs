using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;

namespace Ideax.Entities;

public class User : IdentityUser<Guid>
{
    // Additional profile fields
    public required string FullName { get; set; }
    public UserRole Role { get; set; } = UserRole.Client;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<ServiceRequest> ServiceRequests { get; set; } = new List<ServiceRequest>();
}

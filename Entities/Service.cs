using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ideax.Entities;

public class Service
{
    public Guid Id { get; set; }
    [Required]
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ServiceCategory Category { get; set; } = ServiceCategory.Other;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<ServiceRequest> Requests { get; set; } = new List<ServiceRequest>();
}

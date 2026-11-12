
using System;

namespace VegetableShopAPI.Models;

public class EmployeeActivityLog
{
    public long Id { get; set; }

    public int? EmployeeId { get; set; }

    public int? ActorUserId { get; set; }

    public string Action { get; set; } = "";

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

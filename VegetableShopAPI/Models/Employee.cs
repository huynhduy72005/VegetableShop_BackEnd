
using System;
using System.ComponentModel.DataAnnotations;

namespace VegetableShopAPI.Models;

public class Employee
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string EmployeeCode { get; set; } = "";

    [Required, MaxLength(150)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, MaxLength(255)]
    public string Email { get; set; } = "";

    [Required, MaxLength(20)]
    public string Phone { get; set; } = "";

    [Required, MaxLength(100)]
    public string Position { get; set; } = "";

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Active";

    public int? UserId { get; set; }

    public bool CanManageProducts { get; set; }
    public bool CanManageOrders { get; set; }
    public bool CanManageInventory { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

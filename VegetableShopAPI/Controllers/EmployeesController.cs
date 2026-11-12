
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class EmployeesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public EmployeesController(ApplicationDbContext db)
    {
        _db = db;
    }

    private int? CurrentUserId =>
        int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id
        ) ? id : null;

    private void Log(int employeeId, string action, string details)
    {
        _db.EmployeeActivityLogs.Add(new EmployeeActivityLog
        {
            EmployeeId = employeeId,
            ActorUserId = CurrentUserId,
            Action = action,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });
    }

    // Lấy danh sách nhân viên
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _db.Employees
            .AsNoTracking()
            .OrderBy(e => e.Id)
            .ToListAsync());
    }

    // Thêm nhân viên
    [HttpPost]
    public async Task<IActionResult> Create(EmployeeInput input)
    {
        // Kiểm tra họ tên có hợp lệ không
        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            return BadRequest(new
            {
                errors = new
                {
                    fullName = new[]
                    {
                    "Vui lòng nhập họ và tên hợp lệ."
                }
                }
            });
        }
        var code = input.EmployeeCode.Trim();
        var email = input.Email.Trim().ToLowerInvariant();

        if (await _db.Employees.AnyAsync(
            e => e.EmployeeCode == code || e.Email == email))
        {
            return Conflict(new
            {
                message = "Mã nhân viên hoặc email đã tồn tại."
            });
        }

        var employee = new Employee
        {
            EmployeeCode = code,
            FullName = input.FullName.Trim(),
            Email = email,
            Phone = input.Phone.Trim(),
            Position = input.Position.Trim(),
            Status = "Active"
        };

        await using var tx = await _db.Database.BeginTransactionAsync();

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        Log(employee.Id, "CREATE_EMPLOYEE", "Tạo hồ sơ nhân viên");
        await _db.SaveChangesAsync();

        await tx.CommitAsync();

        return CreatedAtAction(
            nameof(GetById),
            new { id = employee.Id },
            employee
        );
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);

        return employee == null ? NotFound() : Ok(employee);
    }

    // Chỉnh sửa nhân viên
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, EmployeeInput input)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            return BadRequest(new
            {
                errors = new
                {
                    fullName = new[]
                    {
                "Vui lòng nhập họ và tên hợp lệ."
            }
                }
            });
        }
        var code = input.EmployeeCode.Trim();
        var email = input.Email.Trim().ToLowerInvariant();

        if (await _db.Employees.AnyAsync(e =>
            e.Id != id &&
            (e.EmployeeCode == code || e.Email == email)))
        {
            return Conflict(new
            {
                message = "Mã nhân viên hoặc email đã tồn tại."
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        employee.EmployeeCode = code;
        employee.FullName = input.FullName.Trim();
        employee.Email = email;
        employee.Phone = input.Phone.Trim();
        employee.Position = input.Position.Trim();

        Log(id, "UPDATE_EMPLOYEE", "Cập nhật hồ sơ nhân viên");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(employee);
    }

    // Cập nhật quyền và trạng thái
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(
        int id, PermissionInput input)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee == null) return NotFound();

        if (input.Status != "Active" &&
            input.Status != "Inactive")
            return BadRequest(new { message = "Trạng thái không hợp lệ." });

        await using var tx = await _db.Database.BeginTransactionAsync();

        employee.CanManageProducts = input.CanManageProducts;
        employee.CanManageOrders = input.CanManageOrders;
        employee.CanManageInventory = input.CanManageInventory;
        employee.Status = input.Status;

        Log(id, "UPDATE_PERMISSIONS", "Cập nhật quyền nhân viên");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(employee);
    }

    // Nhật ký hoạt động
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs()
    {
        var logs = await _db.EmployeeActivityLogs
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .Select(l => new
            {
                l.Id,
                l.EmployeeId,
                l.ActorUserId,
                l.Action,
                l.Details,
                l.CreatedAt
            })
            .ToListAsync();

        return Ok(logs);
    }
}


public class EmployeeInput
{
    // Mã nhân viên
    [Required(ErrorMessage = "Vui lòng nhập mã nhân viên.")]
    [RegularExpression(
        @"^NV[0-9]+$",
        ErrorMessage = "Mã nhân viên phải có dạng NV001, NV002..."
    )]
    [MaxLength(20, ErrorMessage = "Mã nhân viên tối đa 20 ký tự.")]
    public string EmployeeCode { get; set; } = "";

    // Họ và tên
    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [MaxLength(150, ErrorMessage = "Họ tên tối đa 150 ký tự.")]
    public string FullName { get; set; } = "";

    // Email
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ.")]
    [MaxLength(255, ErrorMessage = "Email quá dài.")]
    public string Email { get; set; } = "";

    // Số điện thoại
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(
        @"^0[0-9]{9}$",
        ErrorMessage = "Số điện thoại phải bắt đầu bằng 0 và có đúng 10 chữ số."
    )]
    public string Phone { get; set; } = "";

    // Chức vụ
    [Required(ErrorMessage = "Vui lòng chọn chức vụ.")]
    [RegularExpression(
        @"^(Staff|Admin)$",
        ErrorMessage = "Chức vụ chỉ được là Nhân viên hoặc Quản trị viên."
    )]
    public string Position { get; set; } = "";
}


public class PermissionInput
{
    public bool CanManageProducts { get; set; }
    public bool CanManageOrders { get; set; }
    public bool CanManageInventory { get; set; }
    public string Status { get; set; } = "Active";
}

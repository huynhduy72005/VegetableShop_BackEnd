
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class EmployeesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AccountCredentialService _credentials;
    private readonly EmployeeCodeService _employeeCodes;

    public EmployeesController(
        ApplicationDbContext db,
        AccountCredentialService credentials,
        EmployeeCodeService employeeCodes)
    {
        _db = db;
        _credentials = credentials;
        _employeeCodes = employeeCodes;
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

    private IQueryable<EmployeeResponse> GetEmployeeResponses() =>
        _db.Employees
            .AsNoTracking()
            .Select(employee => new EmployeeResponse
            {
                Id = employee.Id,
                EmployeeCode = employee.EmployeeCode,
                FullName = employee.FullName,
                Email = employee.Email,
                Phone = employee.Phone,
                Position = employee.Position,
                Status = employee.Status,
                UserId = _db.Users
                    .Where(user =>
                        (int?)user.Id == employee.UserId ||
                        (employee.UserId == null &&
                            user.Email.ToLower() == employee.Email.ToLower()))
                    .Select(user => (int?)user.Id)
                    .FirstOrDefault(),
                Username = _db.Users
                    .Where(user =>
                        (int?)user.Id == employee.UserId ||
                        (employee.UserId == null &&
                            user.Email.ToLower() == employee.Email.ToLower()))
                    .Select(user => user.Username)
                    .FirstOrDefault(),
                UserRole = _db.Users
                    .Where(user =>
                        (int?)user.Id == employee.UserId ||
                        (employee.UserId == null &&
                            user.Email.ToLower() == employee.Email.ToLower()))
                    .Select(user => user.Role)
                    .FirstOrDefault(),
                AccountIsActive = _db.Users
                    .Where(user =>
                        (int?)user.Id == employee.UserId ||
                        (employee.UserId == null &&
                            user.Email.ToLower() == employee.Email.ToLower()))
                    .Select(user => (bool?)user.IsActive)
                    .FirstOrDefault(),
                MustChangePassword = _db.Users
                    .Where(user =>
                        (int?)user.Id == employee.UserId ||
                        (employee.UserId == null &&
                            user.Email.ToLower() == employee.Email.ToLower()))
                    .Select(user => (bool?)user.MustChangePassword)
                    .FirstOrDefault(),
                CanManageProducts = employee.CanManageProducts,
                CanManageOrders = employee.CanManageOrders,
                CanManageInventory = employee.CanManageInventory
            });

    private static void CopyEmployeeDetailsToUser(Employee employee, User user)
    {
        user.FullName = employee.FullName;
        user.Email = employee.Email;
        user.Phone = employee.Phone;
    }

    private static string RoleFromEmployeeCode(string employeeCode) =>
        employeeCode.StartsWith("AD", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : employeeCode.StartsWith("KH", StringComparison.OrdinalIgnoreCase)
                ? "Customer"
                : "Staff";

    // Lấy danh sách nhân viên
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employees = await GetEmployeeResponses()
            .OrderBy(e => e.Id)
            .ToListAsync();

        var employeesByAccount = await _db.Users
            .AsNoTracking()
            .Where(user => user.Role == "Staff" &&
                !_db.Employees.Any(employee =>
                    employee.UserId == user.Id ||
                    employee.Email.ToLower() == user.Email.ToLower()))
            .Select(user => new EmployeeResponse
            {
                Id = -user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone ?? string.Empty,
                Position = string.Empty,
                Status = "Pending",
                UserId = user.Id,
                Username = user.Username,
                UserRole = user.Role,
                AccountIsActive = user.IsActive,
                MustChangePassword = user.MustChangePassword,
                ProfilePending = true
            })
            .ToListAsync();

        return Ok(employees
            .Concat(employeesByAccount)
            .OrderBy(employee => employee.Id)
            .ToList());
    }

    // Thêm nhân viên
    [HttpPost]
    public async Task<IActionResult> Create(EmployeeInput input)
    {
        var role = input.UserRole;
        if (role != "Admin" && role != "Staff")
        {
            return BadRequest(new { message = "Vai trò nhân viên không hợp lệ." });
        }

        var email = input.Email.Trim().ToLowerInvariant();
        var fullName = input.FullName.Trim();
        var phone = input.Phone.Trim();
        var position = input.Position.Trim();

        if (await _db.Employees.AnyAsync(employee => employee.Email.ToLower() == email))
        {
            return Conflict(new
            {
                message = "Email này đã có hồ sơ nhân viên."
            });
        }

        string? initialPassword = null;
        var requestedUsername = input.Username ?? string.Empty;
        if (input.CreateAccount)
        {
            if (await _db.Users.AnyAsync(user => user.Email.ToLower() == email))
            {
                return Conflict(new
                {
                    message = "Email đã có tài khoản. Hãy chọn không tạo tài khoản mới để liên kết tài khoản hiện có."
                });
            }

            if (!input.GenerateTemporaryPassword &&
                string.IsNullOrWhiteSpace(input.InitialPassword))
            {
                return BadRequest(new
                {
                    message = "Vui lòng nhập mật khẩu ban đầu hoặc chọn tự tạo mật khẩu tạm."
                });
            }

            if (!input.GenerateTemporaryPassword &&
                input.InitialPassword.Length < 8)
            {
                return BadRequest(new
                {
                    message = "Mật khẩu ban đầu phải có ít nhất 8 ký tự."
                });
            }

            if (!Regex.IsMatch(
                requestedUsername,
                @"^[a-zA-Z0-9._-]{3,100}$"))
            {
                return BadRequest(new
                {
                    message = "Tên đăng nhập cần dài 3–100 ký tự (chữ, số, dấu chấm, gạch dưới hoặc gạch ngang)."
                });
            }

            initialPassword = input.GenerateTemporaryPassword
                ? AccountCredentialService.GenerateTemporaryPassword()
                : input.InitialPassword;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        var employee = new Employee
        {
            EmployeeCode = "",
            FullName = fullName,
            Email = email,
            Phone = phone,
            Position = position,
            Status = "Active"
        };

        User? linkedUser = null;
        if (input.CreateAccount)
        {
            var username = await _credentials.CreateUniqueUsernameAsync(
                requestedUsername);
            linkedUser = new User
            {
                Username = username,
                FullName = fullName,
                Email = email,
                Phone = phone,
                Role = role,
                IsActive = input.AccountIsActive,
                MustChangePassword = true
            };
            linkedUser.PasswordHash = _credentials.HashPassword(
                linkedUser,
                initialPassword!);
            _db.Users.Add(linkedUser);
            await _db.SaveChangesAsync();
        }
        else
        {
            linkedUser = await _db.Users.FirstOrDefaultAsync(
                user => user.Email.ToLower() == email);
            if (linkedUser != null &&
                await _db.Employees.AnyAsync(
                    existing => existing.UserId == linkedUser.Id))
            {
                return Conflict(new
                {
                    message = "Tài khoản này đã được liên kết với nhân viên khác."
                });
            }
        }

        try
        {
            employee.EmployeeCode = await _employeeCodes.GenerateAsync(role);
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { message = "Dãy mã nhân viên đã sử dụng hết." });
        }

        if (linkedUser != null)
        {
            employee.UserId = linkedUser.Id;
            if (!input.CreateAccount)
            {
                linkedUser.Role = role;
                CopyEmployeeDetailsToUser(employee, linkedUser);
            }
        }

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        Log(employee.Id, "CREATE_EMPLOYEE", "Tạo hồ sơ nhân viên");
        await _db.SaveChangesAsync();

        await tx.CommitAsync();

        var response = await GetEmployeeResponses()
            .FirstAsync(e => e.Id == employee.Id);
        return CreatedAtAction(
            nameof(GetById),
            new { id = employee.Id },
            new
            {
                employee = response,
                initialCredentials = linkedUser != null && input.CreateAccount
                    ? new
                    {
                        linkedUser.Username,
                        linkedUser.Email,
                        Password = initialPassword
                    }
                    : null
            }
        );
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employee = await GetEmployeeResponses()
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
        var email = input.Email.Trim().ToLowerInvariant();
        var fullName = input.FullName.Trim();
        var phone = input.Phone.Trim();
        var position = input.Position.Trim();

        if (await _db.Employees.AnyAsync(e =>
            e.Id != id &&
            e.Email.ToLower() == email))
        {
            return Conflict(new
            {
                message = "Email này đã có hồ sơ nhân viên."
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        var wasUnlinked = !employee.UserId.HasValue;
        var linkedUser = employee.UserId.HasValue
            ? await _db.Users.FindAsync(employee.UserId.Value)
            : await _db.Users.FirstOrDefaultAsync(
                user => user.Email.ToLower() == email);

        if (linkedUser != null)
        {
            if (await _db.Users.AnyAsync(
                user => user.Id != linkedUser.Id &&
                    user.Email.ToLower() == email))
            {
                return Conflict(new
                {
                    message = "Email này đã được tài khoản khác sử dụng."
                });
            }

            if (await _db.Employees.AnyAsync(
                existing => existing.Id != id &&
                    existing.UserId == linkedUser.Id))
            {
                return Conflict(new
                {
                    message = "Tài khoản này đã được liên kết với nhân viên khác."
                });
            }

            employee.UserId = linkedUser.Id;
            if (wasUnlinked)
                linkedUser.Role = RoleFromEmployeeCode(employee.EmployeeCode);
        }

        employee.FullName = fullName;
        employee.Email = email;
        employee.Phone = phone;
        employee.Position = position;

        if (linkedUser != null)
            CopyEmployeeDetailsToUser(employee, linkedUser);

        Log(id, "UPDATE_EMPLOYEE", "Cập nhật hồ sơ nhân viên");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(await GetEmployeeResponses()
            .FirstAsync(e => e.Id == id));
    }

    // Cập nhật quyền và trạng thái
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> UpdatePermissions(
        int id, PermissionInput input)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(input.UserRole) &&
            input.UserRole != "Admin" &&
            input.UserRole != "Staff" &&
            input.UserRole != "Customer")
        {
            return BadRequest(new { message = "Vai trò tài khoản không hợp lệ." });
        }

        if (input.Status != "Active" &&
            input.Status != "Inactive")
            return BadRequest(new { message = "Trạng thái không hợp lệ." });

        var wasUnlinked = !employee.UserId.HasValue;
        var linkedUser = employee.UserId.HasValue
            ? await _db.Users.FindAsync(employee.UserId.Value)
            : await _db.Users.FirstOrDefaultAsync(
                user => user.Email.ToLower() == employee.Email.ToLower());
        if (linkedUser == null &&
            (!string.IsNullOrWhiteSpace(input.UserRole) ||
                input.AccountIsActive.HasValue))
        {
            return Conflict(new
            {
                message = "Nhân viên chưa liên kết tài khoản nên chưa thể cập nhật quyền hoặc trạng thái tài khoản."
            });
        }

        if (linkedUser != null &&
            await _db.Employees.AnyAsync(
                existing => existing.Id != id &&
                    existing.UserId == linkedUser.Id))
        {
            return Conflict(new
            {
                message = "Tài khoản này đã được liên kết với nhân viên khác."
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        if (linkedUser != null)
        {
            employee.UserId = linkedUser.Id;
            CopyEmployeeDetailsToUser(employee, linkedUser);
        }

        var oldRole = linkedUser?.Role;
        var targetRole = string.IsNullOrWhiteSpace(input.UserRole)
            ? wasUnlinked && linkedUser != null
                ? RoleFromEmployeeCode(employee.EmployeeCode)
                : null
            : input.UserRole;
        if (linkedUser != null && targetRole != null &&
            linkedUser.Role != targetRole)
        {
            try
            {
                employee.EmployeeCode = await _employeeCodes.GenerateAsync(
                    targetRole,
                    employee.Id,
                    employee.EmployeeCode);
            }
            catch (InvalidOperationException)
            {
                return Conflict(new { message = "Dãy mã nhân viên đã sử dụng hết." });
            }

            linkedUser.Role = targetRole;
        }

        employee.CanManageProducts = input.CanManageProducts;
        employee.CanManageOrders = input.CanManageOrders;
        employee.CanManageInventory = input.CanManageInventory;
        employee.Status = input.Status;
        if (linkedUser != null && input.AccountIsActive.HasValue)
            linkedUser.IsActive = input.AccountIsActive.Value;

        Log(
            id,
            "UPDATE_PERMISSIONS",
            oldRole == linkedUser?.Role
                ? "Cập nhật quyền và trạng thái nhân viên"
                : $"Đổi vai trò từ {oldRole} sang {linkedUser?.Role}; mã {employee.EmployeeCode}");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(await GetEmployeeResponses()
            .FirstAsync(e => e.Id == id));
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


public class EmployeeResponse
{
    public int Id { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Position { get; set; } = "";
    public string Status { get; set; } = "";
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string? UserRole { get; set; }
    public bool? AccountIsActive { get; set; }
    public bool? MustChangePassword { get; set; }
    public bool ProfilePending { get; set; }
    public bool CanManageProducts { get; set; }
    public bool CanManageOrders { get; set; }
    public bool CanManageInventory { get; set; }
}


public class EmployeeInput
{
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

    // Chức vụ công việc, không phải vai trò đăng nhập
    [Required(ErrorMessage = "Vui lòng nhập chức vụ.")]
    [MaxLength(100, ErrorMessage = "Chức vụ tối đa 100 ký tự.")]
    public string Position { get; set; } = "";

    [Required]
    public string UserRole { get; set; } = "Staff";

    public bool CreateAccount { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public bool GenerateTemporaryPassword { get; set; } = true;

    public string InitialPassword { get; set; } = string.Empty;

    public bool AccountIsActive { get; set; } = true;
}


public class PermissionInput
{
    public string? UserRole { get; set; }
    public bool? AccountIsActive { get; set; }
    public bool CanManageProducts { get; set; }
    public bool CanManageOrders { get; set; }
    public bool CanManageInventory { get; set; }
    public string Status { get; set; } = "Active";
}

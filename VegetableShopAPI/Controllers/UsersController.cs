using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.Tokens;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IDataProtector _passwordProtector;
        private readonly IConfiguration _configuration;
        private readonly AccountCredentialService _credentials;
        private readonly EmployeeCodeService _employeeCodes;

        public UsersController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            AccountCredentialService credentials,
            EmployeeCodeService employeeCodes)
        {
            _context = context;
            _passwordProtector = dataProtectionProvider.CreateProtector(
                "VegetableShop.PasswordResetRequest.v1");
            _configuration = configuration;
            _credentials = credentials;
            _employeeCodes = employeeCodes;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserInput input)
        {
            var email = input.Email.Trim().ToLowerInvariant();
            if (await _context.Users.AnyAsync(
                user => user.Email.ToLower() == email))
                return BadRequest(new { message = "Email này đã được đăng ký!" });

            var user = new User
            {
                Username = await _credentials.CreateUniqueUsernameAsync(
                    email.Split('@')[0]),
                FullName = input.FullName.Trim(),
                Email = email,
                PasswordHash = string.Empty,
                IsActive = true,
                MustChangePassword = false,
                Role = "Customer"
            };
            user.PasswordHash = _credentials.HashPassword(user, input.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đăng ký thành công!" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginInput loginDto)
        {
            var identifier = (
                string.IsNullOrWhiteSpace(loginDto.Login)
                    ? loginDto.Email
                    : loginDto.Login)?.Trim().ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(identifier))
                return BadRequest(new { message = "Vui lòng nhập tên đăng nhập hoặc email." });

            var user = await _context.Users.FirstOrDefaultAsync(account =>
                account.Username.ToLower() == identifier ||
                account.Email.ToLower() == identifier);
            if (user == null ||
                !user.IsActive ||
                !_credentials.VerifyPassword(
                    user,
                    loginDto.Password,
                    out var hashUpgraded))
            {
                return Unauthorized(new
                {
                    message = "Tên đăng nhập, email hoặc mật khẩu không đúng."
                });
            }

            if (await _context.Employees.AnyAsync(employee =>
                employee.UserId == user.Id &&
                employee.Status != "Active"))
            {
                return Unauthorized(new
                {
                    message = "Tài khoản nhân viên đã bị vô hiệu hóa."
                });
            }

            if (hashUpgraded)
                await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = user.MustChangePassword
                    ? "Đăng nhập thành công. Hãy đổi mật khẩu trước khi tiếp tục."
                    : "Đăng nhập thành công!",
                token = CreateJwtToken(user),
                user = UserResponse(user)
            });
        }

        private string CreateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
                throw new InvalidOperationException("Jwt:Key chưa được cấu hình.");

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("must_change_password", user.MustChangePassword.ToString().ToLowerInvariant()),
                new Claim("password_fingerprint", Convert.ToHexString(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(jwtKey),
                        Encoding.UTF8.GetBytes(user.PasswordHash))))
            };
            var jwt = new JwtSecurityToken(
                issuer: "VegetableShopAPI",
                audience: "VegetableShopReact",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        private static object UserResponse(User user) => new
        {
            user.Id,
            user.Username,
            user.FullName,
            user.Email,
            user.Phone,
            user.Gender,
            user.DateOfBirth,
            user.Role,
            user.IsActive,
            user.MustChangePassword
        };

        [HttpPost("password-reset-eligibility")]
        public async Task<IActionResult> CheckPasswordResetEligibility(
            [FromBody] PasswordResetEligibilityDto dto)
        {
            var userRole = await _context.Users
                .Where(user => user.Email.ToLower() == dto.Email.Trim().ToLower())
                .Select(user => user.Role)
                .FirstOrDefaultAsync();

            if (userRole == null)
                return NotFound(new { message = "Không tìm thấy tài khoản với email này." });

            return Ok(new { requiresAdminRecoveryKey = userRole == "Admin" });
        }

        [HttpPost("password-reset-requests")]
        public async Task<IActionResult> CreatePasswordResetRequest(
            [FromBody] CreatePasswordResetRequestDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(
                account => account.Email.ToLower() == dto.Email.Trim().ToLower());

            if (user == null)
                return Ok(new { success = true, status = "Pending" });

            if (user.Role == "Admin")
            {
                var recoveryKey = _configuration["PasswordRecovery:AdminKey"];
                if (string.IsNullOrEmpty(recoveryKey) ||
                    Encoding.UTF8.GetByteCount(recoveryKey) < 32)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        message = "Mã khôi phục admin chưa được cấu hình an toàn trong backend."
                    });
                }

                if (!FixedTimeEquals(dto.AdminRecoveryKey, recoveryKey))
                {
                    return Unauthorized(new
                    {
                        message = "Mã khôi phục admin không hợp lệ hoặc chưa được cấu hình."
                    });
                }

                user.PasswordHash = _credentials.HashPassword(user, dto.NewPassword);
                user.MustChangePassword = false;
                await _context.SaveChangesAsync();
                return Ok(new
                {
                    success = true,
                    status = "Approved",
                    message = "Mật khẩu admin đã được cập nhật."
                });
            }

            var existingRequest = await _context.PasswordResetRequests
                .Where(request => request.UserId == user.Id && request.Status == "Pending")
                .OrderByDescending(request => request.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (existingRequest != null)
            {
                return Ok(new
                {
                    success = true,
                    status = existingRequest.Status,
                    requestId = existingRequest.Id
                });
            }

            var request = new PasswordResetRequest
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EncryptedPassword = string.Empty,
                NewPasswordHash = _credentials.HashPassword(user, dto.NewPassword),
                Status = "Pending",
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.PasswordResetRequests.Add(request);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                status = request.Status,
                requestId = request.Id
            });
        }

        [HttpGet("password-reset-requests/{id:guid}/status")]
        public async Task<IActionResult> GetPasswordResetRequestStatus(Guid id)
        {
            var status = await _context.PasswordResetRequests
                .Where(request => request.Id == id)
                .Select(request => request.Status)
                .FirstOrDefaultAsync();

            return status == null
                ? NotFound(new { message = "Không tìm thấy yêu cầu." })
                : Ok(new { status });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("password-reset-requests")]
        public async Task<IActionResult> GetPasswordResetRequests()
        {
            var requests = await _context.PasswordResetRequests
                .Where(request => request.Status == "Pending")
                .OrderBy(request => request.CreatedAtUtc)
                .Select(request => new
                {
                    request.Id,
                    request.UserId,
                    request.User.FullName,
                    request.User.Email,
                    request.CreatedAtUtc,
                    request.Status
                })
                .ToListAsync();

            return Ok(requests);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("password-reset-requests/{id:guid}/approve")]
        public async Task<IActionResult> ApprovePasswordResetRequest(Guid id)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            var request = await _context.PasswordResetRequests
                .Include(item => item.User)
                .FirstOrDefaultAsync(item => item.Id == id && item.Status == "Pending");

            if (request == null)
                return NotFound(new { message = "Yêu cầu không tồn tại hoặc đã được xử lý." });

            request.User.PasswordHash = string.IsNullOrEmpty(request.NewPasswordHash)
                ? _credentials.HashPassword(
                    request.User,
                    _passwordProtector.Unprotect(request.EncryptedPassword))
                : request.NewPasswordHash;
            request.User.MustChangePassword = false;
            request.NewPasswordHash = null;
            request.EncryptedPassword = string.Empty;
            request.Status = "Approved";
            request.ReviewedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { success = true, message = "Đã chấp nhận yêu cầu đổi mật khẩu." });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("password-reset-requests/{id:guid}/reject")]
        public async Task<IActionResult> RejectPasswordResetRequest(Guid id)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            var request = await _context.PasswordResetRequests
                .FirstOrDefaultAsync(item => item.Id == id && item.Status == "Pending");
            if (request == null)
                return NotFound(new { message = "Yêu cầu không tồn tại hoặc đã được xử lý." });

            request.EncryptedPassword = string.Empty;
            request.NewPasswordHash = null;
            request.Status = "Rejected";
            request.ReviewedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { success = true, message = "Đã từ chối yêu cầu đổi mật khẩu." });
        }

        [Authorize]
        [HttpPut("change-password/{id:int}")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordDto dto)
        {
            if (!int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var authenticatedUserId) ||
                authenticatedUserId != id)
            {
                return Forbid();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng!" });

            if (!_credentials.VerifyPassword(
                user,
                dto.CurrentPassword,
                out var hashUpgraded))
            {
                if (hashUpgraded)
                    await _context.SaveChangesAsync();
                return BadRequest(new { message = "Mật khẩu hiện tại không đúng." });
            }

            if (_credentials.VerifyPassword(
                user,
                dto.NewPassword,
                out var rehashedCurrentPassword))
            {
                if (hashUpgraded || rehashedCurrentPassword)
                    await _context.SaveChangesAsync();
                return BadRequest(new { message = "Mật khẩu mới phải khác mật khẩu hiện tại." });
            }

            user.PasswordHash = _credentials.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đổi mật khẩu thành công!",
                token = CreateJwtToken(user),
                user = UserResponse(user)
            });
        }

        [Authorize]
        [HttpPut("profile/{id:int}")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateProfileDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var authenticatedUserId) ||
                authenticatedUserId != id)
            {
                return Forbid();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng!" });

            var email = dto.Email.Trim();
            if (await _context.Users.AnyAsync(account => account.Id != id && account.Email == email))
                return Conflict(new { message = "Email này đã được tài khoản khác sử dụng." });

            var phone = Regex.Replace(dto.Phone.Trim(), @"[\s().-]", string.Empty);
            if (!Regex.IsMatch(phone, @"^(?:0(?:3|5|7|8|9)\d{8}|02\d{8,9}|\+84(?:3|5|7|8|9)\d{8}|\+842\d{8,9})$"))
                return BadRequest(new { message = "Số điện thoại Việt Nam không hợp lệ." });

            var allowedGenders = new[] { "Nam", "Nữ", "Khác", "Không muốn tiết lộ" };
            if (!allowedGenders.Contains(dto.Gender))
                return BadRequest(new { message = "Vui lòng chọn giới tính hợp lệ." });

            if (dto.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
                return BadRequest(new { message = "Ngày sinh không thể ở tương lai." });

            user.FullName = dto.FullName.Trim();
            user.Email = email;
            user.Phone = dto.Phone.Trim();
            user.Gender = dto.Gender;
            user.DateOfBirth = dto.DateOfBirth;
            var employee = await _context.Employees.FirstOrDefaultAsync(
                profile => profile.UserId == id);
            if (employee != null)
            {
                if (await _context.Employees.AnyAsync(profile =>
                    profile.Id != employee.Id &&
                    profile.Email.ToLower() == email.ToLower()))
                {
                    return Conflict(new
                    {
                        message = "Email này đã được hồ sơ nhân viên khác sử dụng."
                    });
                }

                employee.FullName = user.FullName;
                employee.Email = user.Email;
                employee.Phone = user.Phone ?? string.Empty;
            }
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật thông tin cá nhân thành công!",
                user = UserResponse(user)
            });
        }

        // GET: api/users (Lấy danh sách tất cả tài khoản - Dành cho Admin)
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(user => new
                {
                    user.Id,
                    user.Username,
                    user.FullName,
                    user.Email,
                    user.Role,
                    user.IsActive,
                    user.MustChangePassword
                })
                .ToListAsync();
            return Ok(users);
        }

        // PUT: api/users/5/role (Cập nhật quyền của tài khoản)
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] RoleUpdateDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            if (dto.Role != "Admin" &&
                dto.Role != "Staff" &&
                dto.Role != "Customer")
            {
                return BadRequest(new { message = "Vai trò tài khoản không hợp lệ." });
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(
                profile => profile.UserId == id);

            await using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (employee != null && user.Role != dto.Role)
            {
                employee.EmployeeCode = await _employeeCodes.GenerateAsync(
                    dto.Role,
                    employee.Id,
                    employee.EmployeeCode);
                _context.EmployeeActivityLogs.Add(new EmployeeActivityLog
                {
                    EmployeeId = employee.Id,
                    ActorUserId = int.TryParse(
                        User.FindFirstValue(ClaimTypes.NameIdentifier),
                        out var actorId) ? actorId : null,
                    Action = "UPDATE_EMPLOYEE_ROLE",
                    Details = $"Đổi vai trò thành {dto.Role}; mã mới {employee.EmployeeCode}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            user.Role = dto.Role;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { success = true, message = "Cập nhật quyền thành công!" });
        }

        // DELETE: api/users/5 (Vô hiệu hóa tài khoản, giữ nguyên lịch sử liên quan)
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            if (!user.IsActive)
            {
                return Ok(new
                {
                    success = true,
                    message = "Tài khoản đã được vô hiệu hóa."
                });
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(profile => profile.UserId == id);
            if (employee != null)
            {
                employee.Status = "Inactive";
                _context.EmployeeActivityLogs.Add(new EmployeeActivityLog
                {
                    EmployeeId = employee.Id,
                    ActorUserId = int.TryParse(
                        User.FindFirstValue(ClaimTypes.NameIdentifier),
                        out var actorId) ? actorId : null,
                    Action = "DEACTIVATE_ACCOUNT",
                    Details = "Vô hiệu hóa tài khoản đăng nhập và hồ sơ nhân viên.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            user.IsActive = false;
            user.MustChangePassword = false;
            await _context.SaveChangesAsync();
            return Ok(new
            {
                success = true,
                message = "Đã vô hiệu hóa tài khoản. Lịch sử đơn hàng được giữ nguyên."
            });
        }

        public class RegisterUserInput
        {
            [Required]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [EmailAddress]
            [StringLength(254)]
            public string Email { get; set; } = string.Empty;

            [Required]
            [MinLength(8)]
            public string Password { get; set; } = string.Empty;
        }

        public class LoginInput
        {
            public string Login { get; set; } = string.Empty;
            public string? Email { get; set; }

            [Required]
            public string Password { get; set; } = string.Empty;
        }

        // Class DTO hỗ trợ nhận dữ liệu đổi quyền
        public class RoleUpdateDto
        {
            public string Role { get; set; } = string.Empty;
        }

        public class ChangePasswordDto
        {
            [Required]
            public string CurrentPassword { get; set; } = string.Empty;

            [Required]
            [MinLength(8)]
            public string NewPassword { get; set; } = string.Empty;
        }

        public class UpdateProfileDto
        {
            [Required]
            [StringLength(100)]
            public string FullName { get; set; } = string.Empty;

            [Required]
            [StringLength(254)]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [StringLength(20)]
            public string Phone { get; set; } = string.Empty;

            [Required]
            public string Gender { get; set; } = string.Empty;

            public DateOnly? DateOfBirth { get; set; }
        }

        public class CreatePasswordResetRequestDto
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;

            [Required]
            [MinLength(8)]
            public string NewPassword { get; set; } = string.Empty;

            public string AdminRecoveryKey { get; set; } = string.Empty;
        }

        public class PasswordResetEligibilityDto
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
        }

        private static bool FixedTimeEquals(string submittedKey, string configuredKey)
        {
            var submittedBytes = Encoding.UTF8.GetBytes(submittedKey ?? string.Empty);
            var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
            return submittedBytes.Length == configuredBytes.Length &&
                CryptographicOperations.FixedTimeEquals(submittedBytes, configuredBytes);
        }

    }
}
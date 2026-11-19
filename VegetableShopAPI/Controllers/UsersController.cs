using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Security.Authentication;
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
        private readonly IEmailService _emailService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            ApplicationDbContext context,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            AccountCredentialService credentials,
            EmployeeCodeService employeeCodes,
            IEmailService emailService,
            ILogger<UsersController> logger)
        {
            _context = context;
            _passwordProtector = dataProtectionProvider.CreateProtector(
                "VegetableShop.PasswordResetRequest.v1");
            _configuration = configuration;
            _credentials = credentials;
            _employeeCodes = employeeCodes;
            _emailService = emailService;
            _logger = logger;
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

            var emailSent = await TrySendWelcomeEmailAsync(user);
            var message = emailSent
                ? "Đăng ký thành công! Email chào mừng đã được gửi."
                : "Đăng ký thành công nhưng chưa gửi được email chào mừng. Vui lòng liên hệ cửa hàng.";
            return Ok(new { success = true, emailSent, message });
        }

        private async Task<bool> TrySendWelcomeEmailAsync(User user)
        {
            try
            {
                var name = HtmlEncoder.Default.Encode(user.FullName);
                var body = $"""
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color: #f3f8f2; padding: 32px 12px; font-family: Arial, Helvetica, sans-serif;">
                        <tr>
                            <td align="center">
                                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width: 100%; max-width: 600px; background-color: #ffffff; border-radius: 16px; overflow: hidden;">
                                    <tr>
                                        <td align="center" style="padding: 28px 24px; background-color: #16834a; color: #ffffff;">
                                            <div style="font-size: 15px; font-weight: bold; letter-spacing: 1px;">🌱 VƯỜN NHÀ</div>
                                            <div style="margin-top: 12px; font-size: 32px;">👋</div>
                                            <h1 style="margin: 8px 0 0; font-size: 24px;">Xin chào, {name}!</h1>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td style="padding: 32px 28px; color: #374151; font-size: 16px; line-height: 1.7;">
                                            <h2 style="margin: 0 0 12px; color: #166534; font-size: 21px;">Chào mừng bạn đến với Vườn Nhà!</h2>
                                            <p style="margin: 0 0 16px;">Tài khoản của bạn đã được tạo thành công. Cảm ơn bạn đã đăng ký và đồng hành cùng chúng tôi.</p>
                                            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin: 22px 0; background-color: #f0fdf4; border-radius: 10px;">
                                                <tr>
                                                    <td style="padding: 16px; color: #166534;">🥬 <strong>Chúc bạn có những trải nghiệm mua sắm thật vui vẻ!</strong></td>
                                                </tr>
                                            </table>
                                            <p style="margin: 0;">Thân mến,<br /><strong style="color: #16834a;">Đội ngũ Vườn Nhà</strong> 💚</p>
                                        </td>
                                    </tr>
                                    <tr>
                                        <td align="center" style="padding: 16px 24px; background-color: #f9fafb; color: #9ca3af; font-size: 12px;">
                                            Email này được gửi tự động sau khi bạn đăng ký tài khoản tại Vườn Nhà.
                                        </td>
                                    </tr>
                                </table>
                            </td>
                        </tr>
                    </table>
                    """;
                var sendTask = _emailService.SendEmailAsync(
                    user.Email,
                    "Chào mừng bạn đến với Vườn Nhà",
                    body);
                var completedTask = await Task.WhenAny(
                    sendTask,
                    Task.Delay(TimeSpan.FromSeconds(10)));

                if (completedTask != sendTask)
                {
                    _logger.LogWarning(
                        "Gửi email chào mừng tới {Email} quá 10 giây.",
                        user.Email);
                    _ = sendTask.ContinueWith(
                        task => _logger.LogError(
                            task.Exception,
                            "Gửi email chào mừng tới {Email} thất bại sau timeout.",
                            user.Email),
                        TaskContinuationOptions.OnlyOnFaulted);
                    return false;
                }

                await sendTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Không gửi được email chào mừng tới {Email}.",
                    user.Email);
                return false;
            }
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
                new Claim("admin_vip", user.IsVipAdmin.ToString().ToLowerInvariant()),
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
            user.IsVipAdmin,
            user.IsActive,
            user.MustChangePassword,
            user.AvatarUrl
        };

        [AllowAnonymous]
        [HttpPost("password-reset/send-otp")]
        public async Task<IActionResult> SendPasswordResetOtp(
            [FromBody] SendPasswordResetOtpDto dto)
        {
            const int resendDelaySeconds = 60;
            var now = DateTime.UtcNow;
            var email = dto.Email.Trim().ToLowerInvariant();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            var user = await _context.Users.FirstOrDefaultAsync(account =>
                account.Email.ToLower() == email && account.IsActive);
            if (user == null)
            {
                await transaction.CommitAsync();
                return Ok(new
                {
                    success = true,
                    message = "Nếu email thuộc tài khoản đang hoạt động, mã OTP sẽ được gửi."
                });
            }

            if (user.IsVipAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Quản trị viên VIP không thể khôi phục mật khẩu bằng OTP."
                });
            }

            var otp = await _context.PasswordResetOtps
                .FirstOrDefaultAsync(item => item.UserId == user.Id);
            if (otp != null &&
                otp.CreatedAtUtc > now.AddSeconds(-resendDelaySeconds))
            {
                await transaction.CommitAsync();
                return Ok(new
                {
                    success = true,
                    message = "Nếu email thuộc tài khoản đang hoạt động, mã OTP sẽ được gửi."
                });
            }

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000)
                .ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var salt = RandomNumberGenerator.GetBytes(16);
            var codeHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(code),
                salt,
                100_000,
                HashAlgorithmName.SHA256,
                32);

            if (otp == null)
            {
                otp = new PasswordResetOtp { UserId = user.Id };
                _context.PasswordResetOtps.Add(otp);
            }

            otp.CodeHash = Convert.ToBase64String(codeHash);
            otp.Salt = Convert.ToBase64String(salt);
            otp.CreatedAtUtc = now;
            otp.ExpiresAtUtc = now.AddMinutes(10);
            otp.UsedAtUtc = null;
            otp.FailedAttempts = 0;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var safeName = HtmlEncoder.Default.Encode(user.FullName);
            var emailBody = $"""
                <p>Xin chào {safeName},</p>
                <p>Mã OTP để đặt lại mật khẩu VegetableShop của bạn là:</p>
                <p style="font-size:28px;font-weight:bold;letter-spacing:8px">{code}</p>
                <p>Mã có hiệu lực trong 10 phút và chỉ sử dụng được một lần.</p>
                <p>Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.</p>
                """;
            try
            {
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Mã OTP đặt lại mật khẩu VegetableShop",
                    emailBody);
            }
            catch (Exception exception) when (
                exception is MailKit.Net.Smtp.SmtpCommandException or
                MailKit.Net.Smtp.SmtpProtocolException or
                IOException or
                SocketException or
                AuthenticationException or
                FormatException or
                ArgumentException)
            {
                otp.UsedAtUtc = DateTime.UtcNow;
                otp.ExpiresAtUtc = DateTime.UtcNow;
                otp.CreatedAtUtc = DateTime.UtcNow.AddSeconds(-(resendDelaySeconds + 1));
                await _context.SaveChangesAsync();
                _logger.LogError(
                    exception,
                    "Failed to send password reset OTP for user {UserId}.",
                    user.Id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    message = "Không gửi được email OTP. Vui lòng kiểm tra cấu hình email hoặc thử lại."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Nếu email thuộc tài khoản đang hoạt động, mã OTP sẽ được gửi."
            });
        }

        [AllowAnonymous]
        [HttpPost("password-reset/confirm")]
        public async Task<IActionResult> ConfirmPasswordReset(
            [FromBody] ConfirmPasswordResetDto dto)
        {
            const int maxFailedAttempts = 5;
            var now = DateTime.UtcNow;
            var email = dto.Email.Trim().ToLowerInvariant();
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            var user = await _context.Users.FirstOrDefaultAsync(account =>
                account.Email.ToLower() == email && account.IsActive);
            var invalidOtp = new { message = "Mã OTP không hợp lệ hoặc đã hết hạn." };
            if (user == null)
                return BadRequest(invalidOtp);

            if (user.IsVipAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Quản trị viên VIP không thể khôi phục mật khẩu bằng OTP."
                });
            }

            var otp = await _context.PasswordResetOtps
                .FirstOrDefaultAsync(item => item.UserId == user.Id);
            if (otp == null || otp.UsedAtUtc != null)
                return BadRequest(invalidOtp);

            if (otp.ExpiresAtUtc <= now || otp.FailedAttempts >= maxFailedAttempts)
            {
                otp.UsedAtUtc = now;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return BadRequest(invalidOtp);
            }

            var expectedHash = Convert.FromBase64String(otp.CodeHash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(dto.Code),
                Convert.FromBase64String(otp.Salt),
                100_000,
                HashAlgorithmName.SHA256,
                32);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                otp.FailedAttempts++;
                if (otp.FailedAttempts >= maxFailedAttempts)
                    otp.UsedAtUtc = now;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return BadRequest(invalidOtp);
            }

            otp.UsedAtUtc = now;
            user.PasswordHash = _credentials.HashPassword(user, dto.NewPassword);
            user.MustChangePassword = false;

            var pendingRequests = await _context.PasswordResetRequests
                .Where(request => request.UserId == user.Id && request.Status == "Pending")
                .ToListAsync();
            foreach (var request in pendingRequests)
            {
                request.EncryptedPassword = string.Empty;
                request.NewPasswordHash = null;
                request.Status = "Rejected";
                request.ReviewedAtUtc = now;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                success = true,
                message = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập bằng mật khẩu mới."
            });
        }

        [HttpPost("password-reset-eligibility")]
        public IActionResult CheckPasswordResetEligibility(
            [FromBody] PasswordResetEligibilityDto dto) =>
            StatusCode(StatusCodes.Status410Gone, new
            {
                message = "Luồng khôi phục cũ đã ngừng hoạt động. Hãy dùng xác minh OTP qua email."
            });

        [HttpPost("password-reset-requests")]
        public IActionResult CreatePasswordResetRequest(
            [FromBody] CreatePasswordResetRequestDto dto) =>
            StatusCode(StatusCodes.Status410Gone, new
            {
                message = "Không còn gửi yêu cầu đổi mật khẩu cho admin. Hãy dùng xác minh OTP qua email."
            });

        [AllowAnonymous]
        [HttpPost("vip/emergency-password-reset")]
        public IActionResult EmergencyVipPasswordReset(
            [FromBody] VipEmergencyPasswordResetDto dto) =>
            StatusCode(StatusCodes.Status410Gone, new
            {
                message = "Luồng khôi phục cũ đã ngừng hoạt động. Hãy dùng xác minh OTP qua email."
            });

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

        [Authorize(Roles = "Admin,AdminVIP")]
        [HttpGet("password-reset-requests")]
        public async Task<IActionResult> GetPasswordResetRequests()
        {
            var requests = await _context.PasswordResetRequests
                .Where(request =>
                    request.Status == "Pending" &&
                    !request.User.IsVipAdmin)
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

        [Authorize(Roles = "Admin,AdminVIP")]
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

            if (request.User.IsVipAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Chỉ quy trình khôi phục khẩn cấp mới được đặt lại mật khẩu Quản trị viên VIP."
                });
            }

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

        [Authorize(Roles = "Admin,AdminVIP")]
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

            if (user.IsVipAdmin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Quản trị viên VIP không thể tự đổi mật khẩu."
                });
            }

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
            if (user.IsVipAdmin &&
                !string.Equals(
                    email,
                    "admin@gmail.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    message = "Email của tài khoản Quản trị viên VIP không thể thay đổi."
                });
            }

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

        // PUT: api/users/avatar/5  (Cập nhật / xóa ảnh đại diện của chính mình)
        // Ảnh gửi lên dạng data URL (đã được frontend thu nhỏ). Gửi avatarUrl = null để xóa ảnh.
        [Authorize]
        [HttpPut("avatar/{id:int}")]
        public async Task<IActionResult> UpdateAvatar(int id, [FromBody] UpdateAvatarDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var authenticatedUserId) ||
                authenticatedUserId != id)
            {
                return Forbid();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng!" });

            if (string.IsNullOrWhiteSpace(dto.AvatarUrl))
            {
                user.AvatarUrl = null;
            }
            else
            {
                var avatar = dto.AvatarUrl.Trim();

                // Chỉ nhận ảnh JPEG/PNG/WEBP dạng base64, tối đa ~300 KB
                if (!Regex.IsMatch(avatar, @"^data:image/(jpeg|png|webp);base64,[A-Za-z0-9+/]+=*$"))
                    return BadRequest(new { message = "Ảnh đại diện không hợp lệ. Chỉ nhận JPG, PNG hoặc WEBP." });

                if (avatar.Length > 400_000)
                    return BadRequest(new { message = "Ảnh quá lớn. Vui lòng chọn ảnh nhỏ hơn." });

                user.AvatarUrl = avatar;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = user.AvatarUrl == null
                    ? "Đã xóa ảnh đại diện."
                    : "Đã cập nhật ảnh đại diện!",
                user = UserResponse(user)
            });
        }

        // GET: api/users (Lấy danh sách tất cả tài khoản - Dành cho Admin)
        [Authorize(Roles = "Admin,AdminVIP")]
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
                    user.IsVipAdmin,
                    user.IsActive,
                    user.MustChangePassword,
                    user.Phone,
                    user.Gender,
                    user.DateOfBirth
                })
                .ToListAsync();
            return Ok(users);
        }

        // PUT: api/users/5/role (Cập nhật quyền của tài khoản)
        [Authorize(Roles = "Admin,AdminVIP")]
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] RoleUpdateDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            var actorId = GetAuthenticatedUserId();
            if (actorId == null || actorId == id || user.IsVipAdmin)
                return Forbid();

            var isVipAdmin = await IsVipAdminAsync(actorId.Value);
            if ((user.Role == "Admin" || user.Role == "AdminVIP" ||
                 dto.Role == "Admin" || dto.Role == "AdminVIP") &&
                !isVipAdmin)
                return Forbid();

            if (dto.Role != "Admin" &&
                dto.Role != "Staff" &&
                dto.Role != "Customer")
            {
                return BadRequest(new { message = "Vai trò tài khoản không hợp lệ." });
            }

            var employee = await _context.Employees.FirstOrDefaultAsync(
                profile => profile.UserId == id);
            var oldRole = user.Role;

            await using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (employee != null && user.Role != dto.Role)
            {
                employee.EmployeeCode = await _employeeCodes.GenerateAsync(
                    dto.Role,
                    employee.Id,
                    employee.EmployeeCode);
            }

            user.Role = dto.Role;
            _context.EmployeeActivityLogs.Add(new EmployeeActivityLog
            {
                EmployeeId = employee?.Id,
                ActorUserId = actorId,
                Action = "UPDATE_USER_ROLE",
                Details = $"Tài khoản {user.Email}: {oldRole} -> {dto.Role}",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { success = true, message = "Cập nhật quyền thành công!" });
        }

        // DELETE: api/users/5 (Vô hiệu hóa tài khoản, giữ nguyên lịch sử liên quan)
        [Authorize(Roles = "Admin,AdminVIP")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            var actorId = GetAuthenticatedUserId();
            if (actorId == null || actorId == id || user.IsVipAdmin)
                return Forbid();

            var isVipAdmin = await IsVipAdminAsync(actorId.Value);
            if ((user.Role == "Admin" || user.Role == "AdminVIP") &&
                !isVipAdmin)
                return Forbid();

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
                    ActorUserId = actorId,
                    Action = "DEACTIVATE_ACCOUNT",
                    Details = "Vô hiệu hóa tài khoản đăng nhập và hồ sơ nhân viên.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            user.IsActive = false;
            user.MustChangePassword = false;
            if (employee == null)
            {
                _context.EmployeeActivityLogs.Add(new EmployeeActivityLog
                {
                    ActorUserId = actorId,
                    Action = "DEACTIVATE_ACCOUNT",
                    Details = $"Vô hiệu hóa tài khoản {user.Email}.",
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();
            return Ok(new
            {
                success = true,
                message = "Đã vô hiệu hóa tài khoản. Lịch sử đơn hàng được giữ nguyên."
            });
        }

        [Authorize(Roles = "Admin,AdminVIP")]
        [HttpPut("{id:int}/restore")]
        public async Task<IActionResult> RestoreUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy người dùng!" });

            var actorId = GetAuthenticatedUserId();
            if (actorId == null || actorId == id)
                return Forbid();

            var isVipAdmin = await IsVipAdminAsync(actorId.Value);
            if (!isVipAdmin)
                return Forbid();

            if (user.IsActive)
            {
                return Ok(new
                {
                    success = true,
                    message = "Tài khoản đang hoạt động."
                });
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(profile => profile.UserId == id);
            if (employee != null)
                employee.Status = "Active";

            user.IsActive = true;
            _context.EmployeeActivityLogs.Add(new EmployeeActivityLog
            {
                EmployeeId = employee?.Id,
                ActorUserId = actorId,
                Action = "RESTORE_ACCOUNT",
                Details = $"Khôi phục tài khoản {user.Email}.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã khôi phục tài khoản."
            });
        }

        private int? GetAuthenticatedUserId() =>
            int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var actorId)
                ? actorId
                : null;

        private Task<bool> IsVipAdminAsync(int actorId) =>
            _context.Users.AnyAsync(account =>
                account.Id == actorId &&
                account.Role == "AdminVIP" &&
                account.IsActive &&
                account.IsVipAdmin);

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

        public class UpdateAvatarDto
        {
            public string? AvatarUrl { get; set; }
        }

        public class SendPasswordResetOtpDto
        {
            [Required]
            [EmailAddress]
            [StringLength(254)]
            public string Email { get; set; } = string.Empty;
        }

        public class ConfirmPasswordResetDto
        {
            [Required]
            [EmailAddress]
            [StringLength(254)]
            public string Email { get; set; } = string.Empty;

            [Required]
            [RegularExpression(@"^\d{6}$")]
            public string Code { get; set; } = string.Empty;

            [Required]
            [MinLength(8)]
            public string NewPassword { get; set; } = string.Empty;
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

        public class VipEmergencyPasswordResetDto
        {
            [Required]
            public string RecoveryKey { get; set; } = string.Empty;

            [Required]
            [MinLength(16)]
            public string NewPassword { get; set; } = string.Empty;
        }

        public class PasswordResetEligibilityDto
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; } = string.Empty;
        }

    }
}
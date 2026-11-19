using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContactController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly EmployeePermissionService _permissions;

        private static readonly string[] AllowedStatuses = { "Pending", "Processing", "Resolved" };

        public ContactController(
            ApplicationDbContext context,
            IEmailService emailService,
            IConfiguration config,
            EmployeePermissionService permissions)
        {
            _context = context;
            _emailService = emailService;
            _config = config;
            _permissions = permissions;
        }

        private async Task<bool> CanManageContactsAsync()
        {
            if (User.IsInRole("Admin") || User.IsInRole("AdminVIP"))
                return true;

            return await _permissions.CanManageProductsAsync(User);
        }

        // POST: api/Contact (Khách hàng gửi liên hệ / khiếu nại)
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> SendContact([FromBody] ContactDto model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.Email))
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });
            }

            var contactRecord = new ContactMessage
            {
                FullName = string.IsNullOrWhiteSpace(model.FullName) ? "Khách hàng" : model.FullName.Trim(),
                Email = model.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                Topic = string.IsNullOrWhiteSpace(model.Subject) ? "Tư vấn" : model.Subject.Trim(),
                Message = (model.Message ?? string.Empty).Trim(),
                Status = "Pending",
                CreatedAt = DateTime.Now
            };

            _context.ContactMessages.Add(contactRecord);
            await _context.SaveChangesAsync();

            try
            {
                string? myAdminEmail = _config["EmailSettings:SenderEmail"];
                if (!string.IsNullOrWhiteSpace(myAdminEmail))
                {
                    string emailSubject = $"[VƯỜN NHÀ - KHÁCH HÀNG LIÊN HỆ]: {contactRecord.Topic}";
                    string emailBody = $@"
                        <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #10b981; border-radius: 10px;'>
                            <h2 style='color: #059669;'>🌱 Có tin nhắn liên hệ mới từ Website Vườn Nhà!</h2>
                            <p><strong>Họ và tên:</strong> {contactRecord.FullName}</p>
                            <p><strong>Số điện thoại:</strong> {contactRecord.Phone ?? "Không cung cấp"}</p>
                            <p><strong>Email khách hàng:</strong> {contactRecord.Email}</p>
                            <p><strong>Chủ đề:</strong> {contactRecord.Topic}</p>
                            <hr style='border: 0; border-top: 1px solid #e5e7eb;' />
                            <p><strong>Nội dung tin nhắn:</strong></p>
                            <p style='background: #f3f4f6; padding: 15px; border-radius: 8px; color: #374151;'>{contactRecord.Message}</p>
                        </div>
                    ";

                    await _emailService.SendEmailAsync(myAdminEmail, emailSubject, emailBody);
                }
            }
            catch
            {
                // Đã lưu thành công vào hệ thống quản trị nên không làm gián đoạn trải nghiệm khách hàng
            }

            return Ok(new
            {
                success = true,
                message = "Gửi thông tin liên hệ thành công! Cửa hàng sẽ phản hồi bạn sớm nhất.",
                id = contactRecord.Id
            });
        }

        // GET: api/Contact?status=&search= (Quản trị: danh sách liên hệ & khiếu nại)
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetContactMessages(
            [FromQuery] string? status = null,
            [FromQuery] string? search = null)
        {
            if (!await CanManageContactsAsync())
                return Forbid();

            var query = _context.ContactMessages.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                var st = status.Trim();
                query = query.Where(c => c.Status == st);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                query = query.Where(c =>
                    c.FullName.ToLower().Contains(q) ||
                    c.Email.ToLower().Contains(q) ||
                    (c.Phone != null && c.Phone.ToLower().Contains(q)) ||
                    c.Topic.ToLower().Contains(q) ||
                    c.Message.ToLower().Contains(q));
            }

            var list = await query
                .OrderBy(c => c.Status == "Pending" ? 0 : c.Status == "Processing" ? 1 : 2)
                .ThenByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .ToListAsync();

            return Ok(list);
        }

        // PUT: api/Contact/5/status (Cập nhật trạng thái xử lý & ghi chú)
        [HttpPut("{id:int}/status")]
        [Authorize]
        public async Task<IActionResult> UpdateContactStatus(int id, [FromBody] UpdateContactStatusDto dto)
        {
            if (!await CanManageContactsAsync())
                return Forbid();

            var contact = await _context.ContactMessages.FirstOrDefaultAsync(c => c.Id == id);
            if (contact == null)
                return NotFound(new { message = "Không tìm thấy thông tin liên hệ!" });

            var normalizedStatus = AllowedStatuses.FirstOrDefault(
                s => string.Equals(s, dto.Status?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Pending";

            contact.Status = normalizedStatus;
            contact.AdminNote = string.IsNullOrWhiteSpace(dto.AdminNote) ? null : dto.AdminNote.Trim();
            contact.ResolvedAt = normalizedStatus == "Resolved" ? DateTime.Now : null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã cập nhật trạng thái xử lý phản hồi.",
                contact
            });
        }

        // DELETE: api/Contact/5 (Xóa liên hệ spam)
        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> DeleteContact(int id)
        {
            if (!await CanManageContactsAsync())
                return Forbid();

            var contact = await _context.ContactMessages.FirstOrDefaultAsync(c => c.Id == id);
            if (contact == null)
                return NotFound(new { message = "Không tìm thấy thông tin liên hệ!" });

            _context.ContactMessages.Remove(contact);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xóa tin nhắn liên hệ."
            });
        }
    }
}
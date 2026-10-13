using Microsoft.AspNetCore.Mvc;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ContactController : ControllerBase
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;

        public ContactController(IEmailService emailService, IConfiguration config)
        {
            _emailService = emailService;
            _config = config;
        }

        [HttpPost]
        public async Task<IActionResult> SendContact([FromBody] ContactDto model)
        {
            if (model == null || string.IsNullOrEmpty(model.Email))
            {
                return BadRequest(new { message = "Dữ liệu không hợp lệ." });
            }

            try
            {
                string myAdminEmail = _config["EmailSettings:SenderEmail"]!;

                string emailSubject = $"[VƯỜN NHÀ - KHÁCH HÀNG LIÊN HỆ]: {model.Subject}";
                string emailBody = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; border: 1px solid #10b981; border-radius: 10px;'>
                        <h2 style='color: #059669;'>🌱 Có tin nhắn liên hệ mới từ Website Vườn Nhà!</h2>
                        <p><strong>Họ và tên:</strong> {model.FullName}</p>
                        <p><strong>Số điện thoại:</strong> {model.Phone}</p>
                        <p><strong>Email khách hàng:</strong> {model.Email}</p>
                        <p><strong>Chủ đề:</strong> {model.Subject}</p>
                        <hr style='border: 0; border-top: 1px solid #e5e7eb;' />
                        <p><strong>Nội dung tin nhắn:</strong></p>
                        <p style='background: #f3f4f6; padding: 15px; border-radius: 8px; color: #374151;'>{model.Message}</p>
                    </div>
                ";

                await _emailService.SendEmailAsync(myAdminEmail, emailSubject, emailBody);

                return Ok(new { message = "Gửi thông tin liên hệ thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Gửi mail thất bại: " + ex.Message });
            }
        }
    }
}

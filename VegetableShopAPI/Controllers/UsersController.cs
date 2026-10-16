using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public UsersController(ApplicationDbContext context) => _context = context;

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] User user)
        {
            if (await _context.Users.AnyAsync(u => u.Email == user.Email))
                return BadRequest(new { message = "Email này đã được đăng ký!" });

            user.Role = "Customer";
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đăng ký thành công!" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] User loginDto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == loginDto.Email && u.Password == loginDto.Password);
            if (user == null) return Unauthorized(new { message = "Email hoặc mật khẩu không đúng!" });
            return Ok(new { success = true, message = "Đăng nhập thành công!", user });
        }

        // GET: api/users (Lấy danh sách tất cả tài khoản - Dành cho Admin)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetAllUsers()
        {
            return await _context.Users.ToListAsync();
        }

        // PUT: api/users/5/role (Cập nhật quyền của tài khoản)
        [HttpPut("{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] RoleUpdateDto dto)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            user.Role = dto.Role; // Cập nhật quyền mới (Admin, Staff, Customer)
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật quyền thành công!" });
        }

        // DELETE: api/users/5 (Xóa tài khoản)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound(new { message = "Không tìm thấy người dùng!" });

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Xóa tài khoản thành công!" });
        }

        // Class DTO hỗ trợ nhận dữ liệu đổi quyền
        public class RoleUpdateDto
        {
            public string Role { get; set; } = string.Empty;
        }



    }
}
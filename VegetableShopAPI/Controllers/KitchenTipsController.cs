using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KitchenTipsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public KitchenTipsController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        private async Task<bool> CanManageContentAsync()
        {
            if (User.IsInRole("Admin") || User.IsInRole("AdminVIP"))
                return true;

            return await _permissions.CanManageProductsAsync(User);
        }

        // GET: api/KitchenTips?category=&search= (Dành cho khách hàng xem các bài đã xuất bản)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublishedTips(
            [FromQuery] string? category = null,
            [FromQuery] string? search = null)
        {
            var query = _context.KitchenTips
                .AsNoTracking()
                .Where(t => t.IsPublished);

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                query = query.Where(t => t.Category == cat);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Title.ToLower().Contains(q) ||
                    (t.Summary != null && t.Summary.ToLower().Contains(q)) ||
                    t.Content.ToLower().Contains(q));
            }

            var tips = await query
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .ToListAsync();

            return Ok(tips);
        }

        // GET: api/KitchenTips/5 (Xem chi tiết 1 bài viết & tăng lượt đọc)
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetTipById(int id)
        {
            var tip = await _context.KitchenTips.FirstOrDefaultAsync(t => t.Id == id);
            if (tip == null)
                return NotFound(new { message = "Không tìm thấy bài viết!" });

            if (!tip.IsPublished && !(User.Identity?.IsAuthenticated == true && await CanManageContentAsync()))
                return NotFound(new { message = "Bài viết hiện không khả dụng." });

            tip.ViewCount += 1;
            await _context.SaveChangesAsync();

            return Ok(tip);
        }

        // GET: api/KitchenTips/admin?search=&category=&isPublished= (Quản trị: lấy tất cả bài viết)
        [HttpGet("admin")]
        [Authorize]
        public async Task<IActionResult> GetAdminTips(
            [FromQuery] string? search = null,
            [FromQuery] string? category = null,
            [FromQuery] bool? isPublished = null)
        {
            if (!await CanManageContentAsync())
                return Forbid();

            var query = _context.KitchenTips.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Title.ToLower().Contains(q) ||
                    (t.Summary != null && t.Summary.ToLower().Contains(q)) ||
                    t.AuthorName.ToLower().Contains(q));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim();
                query = query.Where(t => t.Category == cat);
            }

            if (isPublished.HasValue)
            {
                query = query.Where(t => t.IsPublished == isPublished.Value);
            }

            var list = await query
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .ToListAsync();

            return Ok(list);
        }

        // POST: api/KitchenTips (Đăng bài mẹo nhà bếp mới)
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateTip([FromBody] UpsertKitchenTipDto dto)
        {
            if (!await CanManageContentAsync())
                return Forbid();

            var title = (dto.Title ?? string.Empty).Trim();
            var content = (dto.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                return BadRequest(new { message = "Vui lòng nhập đầy đủ tiêu đề và nội dung bài viết." });

            var authorName = (dto.AuthorName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(authorName))
            {
                if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
                {
                    var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);
                    authorName = !string.IsNullOrWhiteSpace(user?.FullName) ? user.FullName : "Bếp Vườn Nhà";
                }
                else
                {
                    authorName = "Bếp Vườn Nhà";
                }
            }

            var tip = new KitchenTip
            {
                Title = title,
                Summary = string.IsNullOrWhiteSpace(dto.Summary) ? null : dto.Summary.Trim(),
                Content = content,
                Image = string.IsNullOrWhiteSpace(dto.Image) ? null : dto.Image.Trim(),
                Category = string.IsNullOrWhiteSpace(dto.Category) ? "Mẹo bảo quản" : dto.Category.Trim(),
                AuthorName = authorName,
                IsPublished = dto.IsPublished,
                CreatedAt = DateTime.Now
            };

            _context.KitchenTips.Add(tip);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã đăng bài viết mới thành công!",
                tip
            });
        }

        // PUT: api/KitchenTips/5 (Cập nhật bài viết)
        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateTip(int id, [FromBody] UpsertKitchenTipDto dto)
        {
            if (!await CanManageContentAsync())
                return Forbid();

            var tip = await _context.KitchenTips.FirstOrDefaultAsync(t => t.Id == id);
            if (tip == null)
                return NotFound(new { message = "Không tìm thấy bài viết!" });

            var title = (dto.Title ?? string.Empty).Trim();
            var content = (dto.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                return BadRequest(new { message = "Vui lòng nhập đầy đủ tiêu đề và nội dung bài viết." });

            tip.Title = title;
            tip.Summary = string.IsNullOrWhiteSpace(dto.Summary) ? null : dto.Summary.Trim();
            tip.Content = content;
            tip.Image = string.IsNullOrWhiteSpace(dto.Image) ? null : dto.Image.Trim();
            tip.Category = string.IsNullOrWhiteSpace(dto.Category) ? "Mẹo bảo quản" : dto.Category.Trim();
            if (!string.IsNullOrWhiteSpace(dto.AuthorName))
                tip.AuthorName = dto.AuthorName.Trim();
            tip.IsPublished = dto.IsPublished;
            tip.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã cập nhật bài viết thành công!",
                tip
            });
        }

        // DELETE: api/KitchenTips/5 (Xóa bài viết)
        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> DeleteTip(int id)
        {
            if (!await CanManageContentAsync())
                return Forbid();

            var tip = await _context.KitchenTips.FirstOrDefaultAsync(t => t.Id == id);
            if (tip == null)
                return NotFound(new { message = "Không tìm thấy bài viết!" });

            _context.KitchenTips.Remove(tip);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xóa bài viết."
            });
        }
    }
}
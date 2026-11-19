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
    public class ReviewsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public ReviewsController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        private async Task<bool> CanModerateReviewsAsync()
        {
            if (User.IsInRole("Admin") || User.IsInRole("AdminVIP"))
                return true;

            return await _permissions.CanManageProductsAsync(User);
        }

        // GET: api/Reviews/product/5 (Lấy danh sách đánh giá đang hiển thị của 1 sản phẩm)
        [HttpGet("product/{productId:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetReviewsByProduct(int productId)
        {
            var rawReviews = await (
                from r in _context.ProductReviews.AsNoTracking()
                where r.ProductId == productId && !r.IsHidden
                join u in _context.Users.AsNoTracking() on r.UserId equals u.Id into uGroup
                from user in uGroup.DefaultIfEmpty()
                orderby r.CreatedAt descending, r.Id descending
                select new
                {
                    r.Id,
                    r.ProductId,
                    r.UserId,
                    CustomerName = user != null && !string.IsNullOrWhiteSpace(user.FullName)
                        ? user.FullName
                        : (user != null ? user.Username : "Khách hàng"),
                    r.Rating,
                    r.Comment,
                    r.AdminReply,
                    r.RepliedAt,
                    r.CreatedAt
                }).ToListAsync();

            var totalReviews = rawReviews.Count;
            var averageRating = totalReviews > 0
                ? Math.Round(rawReviews.Average(x => x.Rating), 1)
                : 0;

            return Ok(new
            {
                productId,
                averageRating,
                totalReviews,
                reviews = rawReviews
            });
        }

        // POST: api/Reviews (Khách hàng gửi đánh giá sản phẩm)
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateReview([FromBody] CreateProductReviewDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized(new { message = "Vui lòng đăng nhập để gửi đánh giá." });

            var productExists = await _context.Products.AnyAsync(p => p.Id == dto.ProductId);
            if (!productExists)
                return NotFound(new { message = "Không tìm thấy sản phẩm cần đánh giá!" });

            var comment = (dto.Comment ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(comment))
                return BadRequest(new { message = "Vui lòng nhập nội dung nhận xét." });

            var review = new ProductReview
            {
                ProductId = dto.ProductId,
                UserId = userId,
                Rating = Math.Clamp(dto.Rating, 1, 5),
                Comment = comment,
                IsHidden = false,
                CreatedAt = DateTime.Now
            };

            _context.ProductReviews.Add(review);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cảm ơn bạn đã gửi đánh giá sản phẩm!",
                review
            });
        }

        // GET: api/Reviews/admin?search=&rating=&isHidden= (Quản trị: duyệt/ẩn & phản hồi đánh giá)
        [HttpGet("admin")]
        [Authorize]
        public async Task<IActionResult> GetAdminReviews(
            [FromQuery] string? search = null,
            [FromQuery] int? rating = null,
            [FromQuery] bool? isHidden = null)
        {
            if (!await CanModerateReviewsAsync())
                return Forbid();

            var query =
                from r in _context.ProductReviews.AsNoTracking()
                join p in _context.Products.AsNoTracking() on r.ProductId equals p.Id
                join u in _context.Users.AsNoTracking() on r.UserId equals u.Id into uGroup
                from user in uGroup.DefaultIfEmpty()
                select new
                {
                    r.Id,
                    r.ProductId,
                    ProductName = p.Name,
                    ProductImage = p.Image,
                    r.UserId,
                    CustomerName = user != null && !string.IsNullOrWhiteSpace(user.FullName)
                        ? user.FullName
                        : (user != null ? user.Username : "Khách hàng"),
                    CustomerEmail = user != null ? user.Email : null,
                    r.Rating,
                    r.Comment,
                    r.AdminReply,
                    r.RepliedAt,
                    r.IsHidden,
                    r.CreatedAt
                };

            if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
            {
                query = query.Where(x => x.Rating == rating.Value);
            }

            if (isHidden.HasValue)
            {
                query = query.Where(x => x.IsHidden == isHidden.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                query = query.Where(x =>
                    x.ProductName.ToLower().Contains(q) ||
                    x.CustomerName.ToLower().Contains(q) ||
                    x.Comment.ToLower().Contains(q));
            }

            var list = await query
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return Ok(list);
        }

        // PUT: api/Reviews/5/visibility (Duyệt hiển thị / Ẩn bình luận xấu)
        [HttpPut("{id:int}/visibility")]
        [Authorize]
        public async Task<IActionResult> UpdateVisibility(int id, [FromBody] UpdateReviewVisibilityDto dto)
        {
            if (!await CanModerateReviewsAsync())
                return Forbid();

            var review = await _context.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null)
                return NotFound(new { message = "Không tìm thấy đánh giá!" });

            review.IsHidden = dto.IsHidden;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = review.IsHidden
                    ? "Đã ẩn đánh giá khỏi trang sản phẩm."
                    : "Đã duyệt hiển thị đánh giá trên trang sản phẩm.",
                review.Id,
                review.IsHidden
            });
        }

        // PUT: api/Reviews/5/reply (Cửa hàng phản hồi đánh giá của khách)
        [HttpPut("{id:int}/reply")]
        [Authorize]
        public async Task<IActionResult> ReplyReview(int id, [FromBody] ReplyReviewDto dto)
        {
            if (!await CanModerateReviewsAsync())
                return Forbid();

            var review = await _context.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null)
                return NotFound(new { message = "Không tìm thấy đánh giá!" });

            var replyText = string.IsNullOrWhiteSpace(dto.AdminReply) ? null : dto.AdminReply.Trim();
            review.AdminReply = replyText;
            review.RepliedAt = replyText != null ? DateTime.Now : null;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = replyText != null
                    ? "Đã lưu phản hồi của cửa hàng."
                    : "Đã xóa phản hồi.",
                review.Id,
                review.AdminReply,
                review.RepliedAt
            });
        }

        // DELETE: api/Reviews/5 (Xóa vĩnh viễn đánh giá vi phạm)
        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> DeleteReview(int id)
        {
            if (!await CanModerateReviewsAsync())
                return Forbid();

            var review = await _context.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null)
                return NotFound(new { message = "Không tìm thấy đánh giá!" });

            _context.ProductReviews.Remove(review);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Đã xóa đánh giá."
            });
        }
    }
}
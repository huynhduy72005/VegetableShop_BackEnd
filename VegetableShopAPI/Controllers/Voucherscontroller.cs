using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VouchersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public VouchersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Tự động khởi tạo 3 mã mẫu cho Vườn Nhà nếu bảng Vouchers đang trống
        private async Task EnsureSeedVouchersAsync()
        {
            if (await _context.Vouchers.AnyAsync()) return;

            var now = DateTime.UtcNow;
            var samples = new List<Voucher>
            {
                new Voucher
                {
                    Code = "VUONNHA20",
                    Title = "Giảm 20.000đ cho đơn rau củ từ 120.000đ",
                    Description = "Áp dụng cho toàn bộ rau hữu cơ và trái cây tươi theo mùa.",
                    DiscountType = "Fixed",
                    DiscountValue = 20000,
                    MinOrderValue = 120000,
                    UsageLimit = 500,
                    StartDate = now.AddDays(-1),
                    EndDate = now.AddMonths(6),
                    IsActive = true,
                    IsPersonal = false
                },
                new Voucher
                {
                    Code = "FREESHIP15",
                    Title = "Miễn phí vận chuyển 15.000đ",
                    Description = "Hỗ trợ phí giao hàng tận nhà cho đơn từ 80.000đ.",
                    DiscountType = "Fixed",
                    DiscountValue = 15000,
                    MinOrderValue = 80000,
                    UsageLimit = 500,
                    StartDate = now.AddDays(-1),
                    EndDate = now.AddMonths(6),
                    IsActive = true,
                    IsPersonal = false
                },
                new Voucher
                {
                    Code = "XANHTUOI15",
                    Title = "Giảm 15% tối đa 35.000đ",
                    Description = "Ưu đãi đặc biệt cho đơn hàng từ 200.000đ.",
                    DiscountType = "Percent",
                    DiscountValue = 15,
                    MaxDiscountAmount = 35000,
                    MinOrderValue = 200000,
                    UsageLimit = 300,
                    StartDate = now.AddDays(-1),
                    EndDate = now.AddMonths(6),
                    IsActive = true,
                    IsPersonal = false
                }
            };

            _context.Vouchers.AddRange(samples);
            await _context.SaveChangesAsync();
        }

        private static string ResolveStatus(UserVoucher item, DateTime now)
        {
            if (item.IsUsed) return "Used";
            var v = item.Voucher;
            if (v == null || !v.IsActive || v.EndDate < now) return "Expired";
            if (v.UsedCount >= v.UsageLimit) return "OutOfStock";
            return "Available";
        }

        private static decimal CalculateDiscount(Voucher voucher, decimal subtotal)
        {
            if (subtotal < voucher.MinOrderValue) return 0;

            if (string.Equals(voucher.DiscountType, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                var raw = Math.Round(subtotal * (voucher.DiscountValue / 100m), 0);
                if (voucher.MaxDiscountAmount.HasValue && raw > voucher.MaxDiscountAmount.Value)
                    return voucher.MaxDiscountAmount.Value;
                return Math.Min(raw, subtotal);
            }

            return Math.Min(voucher.DiscountValue, subtotal);
        }

        // ============================================================
        // 1. API QUẢN TRỊ VOUCHER (ADMIN: THÊM / SỬA / BẬT-TẮT / XÓA)
        // ============================================================

        // GET: api/vouchers
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> GetAllVouchers()
        {
            await EnsureSeedVouchersAsync();
            var list = await _context.Vouchers
                .OrderByDescending(v => v.Id)
                .ToListAsync();
            return Ok(list);
        }

        // POST: api/vouchers
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateVoucher([FromBody] UpsertVoucherDto dto)
        {
            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            var title = (dto.Title ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(title))
                return BadRequest(new { message = "Vui lòng nhập đầy đủ mã voucher và tên chương trình." });

            if (dto.DiscountValue <= 0)
                return BadRequest(new { message = "Giá trị giảm phải lớn hơn 0." });

            if (dto.DiscountType == "Percent" && dto.DiscountValue > 100)
                return BadRequest(new { message = "Phần trăm giảm không được vượt quá 100%." });

            if (dto.UsageLimit <= 0)
                return BadRequest(new { message = "Số lượt sử dụng tối đa phải lớn hơn 0." });

            if (await _context.Vouchers.AnyAsync(v => v.Code.ToUpper() == code))
                return BadRequest(new { message = $"Mã voucher “{code}” đã tồn tại." });

            var endDate = dto.EndDate.TimeOfDay == TimeSpan.Zero
                ? dto.EndDate.Date.AddDays(1).AddSeconds(-1)
                : dto.EndDate;

            if (endDate < dto.StartDate)
                return BadRequest(new { message = "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu." });

            var voucher = new Voucher
            {
                Code = code,
                Title = title,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                DiscountType = dto.DiscountType == "Percent" ? "Percent" : "Fixed",
                DiscountValue = dto.DiscountValue,
                MaxDiscountAmount = dto.DiscountType == "Percent" && dto.MaxDiscountAmount > 0
                    ? dto.MaxDiscountAmount
                    : null,
                MinOrderValue = Math.Max(0, dto.MinOrderValue),
                UsageLimit = dto.UsageLimit,
                UsedCount = 0,
                StartDate = dto.StartDate,
                EndDate = endDate,
                IsActive = dto.IsActive,
                IsPersonal = dto.IsPersonal
            };

            _context.Vouchers.Add(voucher);
            await _context.SaveChangesAsync();
            return Ok(voucher);
        }

        // PUT: api/vouchers/{id}
        [Authorize(Roles = "Admin")]
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateVoucher(int id, [FromBody] UpsertVoucherDto dto)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            var title = (dto.Title ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(title))
                return BadRequest(new { message = "Vui lòng nhập đầy đủ mã voucher và tên chương trình." });

            if (dto.DiscountValue <= 0)
                return BadRequest(new { message = "Giá trị giảm phải lớn hơn 0." });

            if (dto.DiscountType == "Percent" && dto.DiscountValue > 100)
                return BadRequest(new { message = "Phần trăm giảm không được vượt quá 100%." });

            if (dto.UsageLimit < voucher.UsedCount)
                return BadRequest(new { message = $"Giới hạn lượt dùng không được nhỏ hơn số lượt đã dùng ({voucher.UsedCount})." });

            if (await _context.Vouchers.AnyAsync(v => v.Id != id && v.Code.ToUpper() == code))
                return BadRequest(new { message = $"Mã voucher “{code}” đã được dùng cho ưu đãi khác." });

            var endDate = dto.EndDate.TimeOfDay == TimeSpan.Zero
                ? dto.EndDate.Date.AddDays(1).AddSeconds(-1)
                : dto.EndDate;

            if (endDate < dto.StartDate)
                return BadRequest(new { message = "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu." });

            voucher.Code = code;
            voucher.Title = title;
            voucher.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            voucher.DiscountType = dto.DiscountType == "Percent" ? "Percent" : "Fixed";
            voucher.DiscountValue = dto.DiscountValue;
            voucher.MaxDiscountAmount = dto.DiscountType == "Percent" && dto.MaxDiscountAmount > 0
                ? dto.MaxDiscountAmount
                : null;
            voucher.MinOrderValue = Math.Max(0, dto.MinOrderValue);
            voucher.UsageLimit = dto.UsageLimit;
            voucher.StartDate = dto.StartDate;
            voucher.EndDate = endDate;
            voucher.IsActive = dto.IsActive;
            voucher.IsPersonal = dto.IsPersonal;

            await _context.SaveChangesAsync();
            return Ok(voucher);
        }

        // DELETE: api/vouchers/{id}
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteVoucher(int id)
        {
            var voucher = await _context.Vouchers.FindAsync(id);
            if (voucher == null)
                return NotFound(new { message = "Không tìm thấy voucher." });

            var hasUsedRecords = voucher.UsedCount > 0 ||
                await _context.UserVouchers.AnyAsync(uv => uv.VoucherId == id && uv.IsUsed);

            if (hasUsedRecords)
            {
                return BadRequest(new
                {
                    message = "Voucher này đã có khách hàng sử dụng nên không thể xóa. Bạn có thể bấm biểu tượng con mắt để tắt kích hoạt."
                });
            }

            var savedRecords = await _context.UserVouchers
                .Where(uv => uv.VoucherId == id)
                .ToListAsync();

            if (savedRecords.Count > 0)
                _context.UserVouchers.RemoveRange(savedRecords);

            _context.Vouchers.Remove(voucher);
            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Đã xóa voucher." });
        }

        // ============================================================
        // 2. API KHÁCH HÀNG (VÍ VOUCHER & ÁP DỤNG KHI THANH TOÁN)
        // ============================================================

        // GET: api/vouchers/my
        [Authorize]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyVouchers()
        {
            await EnsureSeedVouchersAsync();

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var now = DateTime.UtcNow;

            var savedItems = await _context.UserVouchers
                .Include(uv => uv.Voucher)
                .Where(uv => uv.UserId == userId)
                .OrderByDescending(uv => uv.SavedAt)
                .ToListAsync();

            var savedVoucherIds = savedItems.Select(uv => uv.VoucherId).ToHashSet();

            var myWallet = savedItems
                .Where(uv => uv.Voucher != null)
                .Select(uv => new
                {
                    userVoucherId = uv.Id,
                    voucherId = uv.Voucher!.Id,
                    code = uv.Voucher.Code,
                    title = uv.Voucher.Title,
                    description = uv.Voucher.Description,
                    discountType = uv.Voucher.DiscountType,
                    discountValue = uv.Voucher.DiscountValue,
                    maxDiscountAmount = uv.Voucher.MaxDiscountAmount,
                    minOrderValue = uv.Voucher.MinOrderValue,
                    endDate = uv.Voucher.EndDate,
                    isPersonal = uv.Voucher.IsPersonal,
                    isUsed = uv.IsUsed,
                    savedAt = uv.SavedAt,
                    status = ResolveStatus(uv, now)
                })
                .ToList();

            var discoverable = await _context.Vouchers
                .Where(v => v.IsActive
                    && !v.IsPersonal
                    && v.StartDate <= now
                    && v.EndDate >= now
                    && v.UsedCount < v.UsageLimit
                    && !savedVoucherIds.Contains(v.Id))
                .OrderBy(v => v.MinOrderValue)
                .Select(v => new
                {
                    voucherId = v.Id,
                    code = v.Code,
                    title = v.Title,
                    description = v.Description,
                    discountType = v.DiscountType,
                    discountValue = v.DiscountValue,
                    maxDiscountAmount = v.MaxDiscountAmount,
                    minOrderValue = v.MinOrderValue,
                    endDate = v.EndDate,
                    remaining = v.UsageLimit - v.UsedCount
                })
                .ToListAsync();

            return Ok(new { myWallet, discoverable });
        }

        // POST: api/vouchers/claim
        [Authorize]
        [HttpPost("claim")]
        public async Task<IActionResult> ClaimVoucher([FromBody] ClaimVoucherDto dto)
        {
            await EnsureSeedVouchersAsync();

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
                return BadRequest(new { message = "Vui lòng nhập mã voucher." });

            var now = DateTime.UtcNow;
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Code.ToUpper() == code);

            if (voucher == null || !voucher.IsActive)
                return NotFound(new { message = "Mã giảm giá không tồn tại hoặc đã ngừng áp dụng." });

            if (voucher.EndDate < now)
                return BadRequest(new { message = "Mã giảm giá này đã hết hạn." });

            if (voucher.UsedCount >= voucher.UsageLimit)
                return BadRequest(new { message = "Mã giảm giá này đã hết lượt sử dụng." });

            var alreadySaved = await _context.UserVouchers
                .AnyAsync(uv => uv.UserId == userId && uv.VoucherId == voucher.Id);

            if (alreadySaved)
                return BadRequest(new { message = "Mã này đã có trong Ví Voucher của bạn rồi." });

            _context.UserVouchers.Add(new UserVoucher
            {
                UserId = userId,
                VoucherId = voucher.Id,
                SavedAt = now,
                IsUsed = false
            });
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Đã lưu mã {voucher.Code} vào Ví Voucher!" });
        }

        // POST: api/vouchers/validate
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateVoucher([FromBody] ValidateVoucherDto dto)
        {
            await EnsureSeedVouchersAsync();

            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
                return BadRequest(new { message = "Vui lòng nhập mã giảm giá." });

            var now = DateTime.UtcNow;
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Code.ToUpper() == code && v.IsActive);

            if (voucher == null)
                return NotFound(new { message = "Mã giảm giá không hợp lệ." });

            if (voucher.StartDate > now || voucher.EndDate < now)
                return BadRequest(new { message = "Mã giảm giá chưa bắt đầu hoặc đã hết hạn." });

            if (voucher.UsedCount >= voucher.UsageLimit)
                return BadRequest(new { message = "Mã giảm giá đã hết lượt sử dụng." });

            if (dto.Subtotal < voucher.MinOrderValue)
            {
                return BadRequest(new
                {
                    message = $"Đơn hàng tối thiểu {voucher.MinOrderValue:N0} đ để dùng mã này."
                });
            }

            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var userVoucher = await _context.UserVouchers
                    .FirstOrDefaultAsync(uv => uv.UserId == userId && uv.VoucherId == voucher.Id);

                if (userVoucher != null && userVoucher.IsUsed)
                    return BadRequest(new { message = "Bạn đã sử dụng mã giảm giá này rồi." });
            }

            var discountAmount = CalculateDiscount(voucher, dto.Subtotal);

            return Ok(new
            {
                valid = true,
                voucher = new
                {
                    voucher.Id,
                    voucher.Code,
                    voucher.Title,
                    voucher.DiscountType,
                    voucher.DiscountValue,
                    voucher.MaxDiscountAmount,
                    voucher.MinOrderValue
                },
                discountAmount,
                message = $"Áp dụng thành công mã {voucher.Code} (-{discountAmount:N0} đ)!"
            });
        }

        // POST: api/vouchers/mark-used
        [Authorize]
        [HttpPost("mark-used")]
        public async Task<IActionResult> MarkVoucherUsed([FromBody] MarkVoucherUsedDto dto)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
            var voucher = await _context.Vouchers
                .FirstOrDefaultAsync(v => v.Code.ToUpper() == code);

            if (voucher == null) return Ok();

            voucher.UsedCount += 1;

            var userVoucher = await _context.UserVouchers
                .FirstOrDefaultAsync(uv => uv.UserId == userId && uv.VoucherId == voucher.Id);

            if (userVoucher != null)
            {
                userVoucher.IsUsed = true;
                userVoucher.UsedAt = DateTime.UtcNow;
                userVoucher.OrderId = dto.OrderId;
            }
            else
            {
                _context.UserVouchers.Add(new UserVoucher
                {
                    UserId = userId,
                    VoucherId = voucher.Id,
                    SavedAt = DateTime.UtcNow,
                    IsUsed = true,
                    UsedAt = DateTime.UtcNow,
                    OrderId = dto.OrderId
                });
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        public class UpsertVoucherDto
        {
            public string Code { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string DiscountType { get; set; } = "Fixed";
            public decimal DiscountValue { get; set; }
            public decimal? MaxDiscountAmount { get; set; }
            public decimal MinOrderValue { get; set; }
            public int UsageLimit { get; set; } = 100;
            public DateTime StartDate { get; set; }
            public DateTime EndDate { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsPersonal { get; set; } = false;
        }

        public class ClaimVoucherDto
        {
            public string Code { get; set; } = string.Empty;
        }

        public class ValidateVoucherDto
        {
            public string Code { get; set; } = string.Empty;
            public decimal Subtotal { get; set; }
        }

        public class MarkVoucherUsedDto
        {
            public string Code { get; set; } = string.Empty;
            public int? OrderId { get; set; }
        }
    }
}
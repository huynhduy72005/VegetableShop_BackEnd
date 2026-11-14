using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VegetableShopAPI.Models
{
    public class Voucher
    {
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        // "Fixed" (giảm tiền mặt) hoặc "Percent" (giảm theo %)
        [Required]
        [StringLength(20)]
        public string DiscountType { get; set; } = "Fixed";

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        // Mức giảm tối đa nếu DiscountType == "Percent"
        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxDiscountAmount { get; set; }

        // Giá trị đơn hàng tối thiểu để áp dụng
        [Column(TypeName = "decimal(18,2)")]
        public decimal MinOrderValue { get; set; }

        public int UsageLimit { get; set; } = 100;
        public int UsedCount { get; set; } = 0;

        public DateTime StartDate { get; set; } = DateTime.UtcNow.AddDays(-1);
        public DateTime EndDate { get; set; } = DateTime.UtcNow.AddMonths(3);

        public bool IsActive { get; set; } = true;

        // true: Mã tặng riêng cho khách; false: Mã công khai để khách tự lưu
        public bool IsPersonal { get; set; } = false;
    }

    public class UserVoucher
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User? User { get; set; }

        public int VoucherId { get; set; }
        public Voucher? Voucher { get; set; }

        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public bool IsUsed { get; set; } = false;
        public DateTime? UsedAt { get; set; }
        public int? OrderId { get; set; }
    }

    public record VoucherEvaluationResult(bool Ok, string Message, decimal Discount);

    public static class VoucherRules
    {
        public static VoucherEvaluationResult Evaluate(Voucher? voucher, decimal subtotal, DateTime now)
        {
            if (voucher == null || !voucher.IsActive)
                return new VoucherEvaluationResult(false, "Mã giảm giá không tồn tại hoặc đã ngừng áp dụng.", 0);

            var utcNow = now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();

            if (voucher.StartDate > utcNow)
                return new VoucherEvaluationResult(false, "Mã giảm giá chưa đến thời gian áp dụng.", 0);

            if (voucher.EndDate < utcNow)
                return new VoucherEvaluationResult(false, "Mã giảm giá đã hết hạn.", 0);

            if (voucher.UsedCount >= voucher.UsageLimit)
                return new VoucherEvaluationResult(false, "Mã giảm giá đã hết lượt sử dụng.", 0);

            if (subtotal < voucher.MinOrderValue)
                return new VoucherEvaluationResult(false, $"Đơn hàng tối thiểu {voucher.MinOrderValue:N0} đ để dùng mã này.", 0);

            decimal discount;
            if (string.Equals(voucher.DiscountType, "Percent", StringComparison.OrdinalIgnoreCase))
            {
                discount = Math.Round(subtotal * (voucher.DiscountValue / 100m), 0);
                if (voucher.MaxDiscountAmount.HasValue && discount > voucher.MaxDiscountAmount.Value)
                    discount = voucher.MaxDiscountAmount.Value;
                discount = Math.Min(discount, subtotal);
            }
            else
            {
                discount = Math.Min(voucher.DiscountValue, subtotal);
            }

            return new VoucherEvaluationResult(true, "Áp dụng mã thành công.", discount);
        }
    }
}
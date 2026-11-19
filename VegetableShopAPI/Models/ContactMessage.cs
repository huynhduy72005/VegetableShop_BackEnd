using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VegetableShopAPI.Models
{
    [Table("ContactMessages")]
    public class ContactMessage
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? Phone { get; set; }

        [Required]
        [MaxLength(150)]
        public string Topic { get; set; } = "Tư vấn";
        // VD: "Khiếu nại đơn hàng" | "Tư vấn sản phẩm" | "Góp ý dịch vụ" | "Hợp tác nhà vườn"

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Pending";
        // Giá trị chuẩn: "Pending" (Chờ xử lý) | "Processing" (Đang xử lý) | "Resolved" (Đã giải quyết)

        [MaxLength(1000)]
        public string? AdminNote { get; set; } // Ghi chú hướng xử lý của nhân viên / Admin

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ResolvedAt { get; set; }
    }

    public class UpdateContactStatusDto
    {
        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        [MaxLength(1000)]
        public string? AdminNote { get; set; }
    }
}
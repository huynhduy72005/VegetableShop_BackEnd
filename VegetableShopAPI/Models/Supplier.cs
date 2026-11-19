using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    [Table("Suppliers")]
    public class Supplier
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // Tên nhà vườn / HTX / Công ty

        [MaxLength(120)]
        public string? ContactPerson { get; set; } // Người đại diện / Chủ vườn

        [MaxLength(25)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(350)]
        public string? Address { get; set; } // Địa chỉ vùng trồng / trang trại

        [Required]
        [MaxLength(60)]
        public string CertificationType { get; set; } = "Thông thường";
        // Giá trị chuẩn: "VietGAP" | "GlobalGAP" | "Hữu cơ (Organic)" | "VietGAP & GlobalGAP" | "Thông thường"

        [MaxLength(100)]
        public string? CertificationCode { get; set; } // Mã số chứng nhận VietGAP / GlobalGAP

        public DateTime? CertificationExpiryDate { get; set; } // Ngày hết hạn chứng nhận

        [MaxLength(500)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true; // true: Đang hợp tác | false: Ngừng hợp tác

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public bool IsCertificationExpired =>
            CertificationExpiryDate.HasValue && CertificationExpiryDate.Value.Date < DateTime.Today;

        [NotMapped]
        public bool IsCertificationNearExpiry =>
            CertificationExpiryDate.HasValue &&
            !IsCertificationExpired &&
            (CertificationExpiryDate.Value.Date - DateTime.Today).Days <= 30;

        [JsonIgnore]
        public List<ProductBatch>? Batches { get; set; }
    }
}
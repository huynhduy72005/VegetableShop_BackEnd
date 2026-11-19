using System.ComponentModel.DataAnnotations;

namespace VegetableShopAPI.Models
{
    public class UpsertSupplierDto
    {
        [Required(ErrorMessage = "Tên nhà cung cấp / nhà vườn không được để trống.")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? ContactPerson { get; set; }

        [MaxLength(25)]
        public string? Phone { get; set; }

        [MaxLength(150)]
        public string? Email { get; set; }

        [MaxLength(350)]
        public string? Address { get; set; }

        [MaxLength(60)]
        public string? CertificationType { get; set; }

        [MaxLength(100)]
        public string? CertificationCode { get; set; }

        public DateTime? CertificationExpiryDate { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class SupplierSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string CertificationType { get; set; } = "Thông thường";
        public string? CertificationCode { get; set; }
        public DateTime? CertificationExpiryDate { get; set; }
        public bool IsCertificationExpired { get; set; }
        public bool IsCertificationNearExpiry { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        // Thống kê tổng hợp từ các lô hàng
        public int TotalBatches { get; set; }
        public int ActiveBatches { get; set; }
        public int TotalSuppliedQuantity { get; set; }
        public DateTime? LastImportDate { get; set; }
    }

    public class SupplierBatchHistoryDto
    {
        public int BatchId { get; set; }
        public string BatchCode { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? VariantName { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public int InitialQuantity { get; set; }
        public int RemainingQuantity { get; set; }
        public decimal? ImportPrice { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public class SupplierDetailDto : SupplierSummaryDto
    {
        public decimal TotalImportValue { get; set; }
        public List<SupplierBatchHistoryDto> Batches { get; set; } = new();
    }
}
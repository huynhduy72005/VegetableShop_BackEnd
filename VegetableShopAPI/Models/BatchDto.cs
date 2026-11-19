using System.ComponentModel.DataAnnotations;

namespace VegetableShopAPI.Models
{
    public class CreateBatchDto
    {
        [Required]
        public int ProductId { get; set; }

        public int? ProductVariantId { get; set; }

        [MaxLength(50)]
        public string? BatchCode { get; set; } // Để trống server sẽ tự sinh mã lô

        public DateTime? ImportDate { get; set; }

        [Required]
        public DateTime ExpiryDate { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng nhập phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [Range(0, double.MaxValue)]
        public decimal? ImportPrice { get; set; }

        [MaxLength(200)]
        public string? SupplierName { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class DisposeBatchDto
    {
        [Required]
        public int BatchId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng hủy phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập lý do xuất hủy.")]
        [MaxLength(300)]
        public string Reason { get; set; } = string.Empty;
    }
}
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    [Table("ProductBatches")]
    public class ProductBatch
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string BatchCode { get; set; } = string.Empty; // VD: LO-20261119-01

        public int ProductId { get; set; }

        // Null nếu sản phẩm không chia biến thể; có giá trị nếu nhập theo từng loại/quy cách
        public int? ProductVariantId { get; set; }

        // Liên kết tới bảng Nhà cung cấp / Nhà vườn
        public int? SupplierId { get; set; }

        public DateTime ImportDate { get; set; } = DateTime.Now;

        public DateTime ExpiryDate { get; set; }

        public int InitialQuantity { get; set; }

        public int RemainingQuantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ImportPrice { get; set; } // Giá vốn nhập lô (tùy chọn)

        [MaxLength(200)]
        public string? SupplierName { get; set; } // Lưu tên nhà vườn tại thời điểm nhập lô

        [MaxLength(500)]
        public string? Note { get; set; }

        [NotMapped]
        public int DaysUntilExpiry => (ExpiryDate.Date - DateTime.Today).Days;

        [NotMapped]
        public string Status
        {
            get
            {
                if (RemainingQuantity <= 0) return "Depleted";   // Đã xuất hết
                if (DaysUntilExpiry < 0) return "Expired";       // Đã hết hạn
                if (DaysUntilExpiry <= 3) return "NearExpiry";   // Cận date (<= 3 ngày)
                return "Valid";                                  // Còn hạn tốt
            }
        }

        [JsonIgnore]
        public Product? Product { get; set; }

        [JsonIgnore]
        public ProductVariant? ProductVariant { get; set; }

        [JsonIgnore]
        public Supplier? Supplier { get; set; }
    }
}
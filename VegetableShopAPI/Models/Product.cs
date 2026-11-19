using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        // Nếu sản phẩm có biến thể: Price = giá thấp nhất, Quantity = tổng tồn kho
        // của các biến thể ĐANG BÁN (server tự tính lại mỗi lần lưu)
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string Unit { get; set; } = string.Empty; // Đơn vị: kg, bó, quả...

        [Column(TypeName = "nvarchar(max)")]
        public string Image { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int CategoryId { get; set; }
        public bool IsVisible { get; set; } = true;

        // Cho phép null để phân biệt:
        //   null = request không nhắc tới biến thể → giữ nguyên biến thể hiện có
        //   []   = xóa hết biến thể
        public List<ProductVariant>? Variants { get; set; }

        [JsonIgnore]
        public List<ProductBatch>? Batches { get; set; }

    }
}
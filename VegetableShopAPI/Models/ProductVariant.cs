using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    [Table("ProductVariants")]
    public class ProductVariant
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public int Quantity { get; set; }

        [MaxLength(30)]
        public string Unit { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(max)")]
        public string? Image { get; set; }

        public bool IsDefault { get; set; } = false;

        public bool IsVisible { get; set; } = true;

        [JsonIgnore]
        public Product? Product { get; set; }

        [JsonIgnore]
        public List<ProductBatch>? Batches { get; set; }
    }
}
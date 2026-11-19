using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VegetableShopAPI.Models
{
    [Table("ProductReviews")]
    public class ProductReview
    {
        public int Id { get; set; }

        public int ProductId { get; set; }

        public int UserId { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; } = 5;

        [Required]
        [MaxLength(1000)]
        public string Comment { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? AdminReply { get; set; } // Phản hồi của cửa hàng

        public DateTime? RepliedAt { get; set; }

        public bool IsHidden { get; set; } = false; // true: Đã ẩn (bình luận xấu/spam)

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonIgnore]
        public Product? Product { get; set; }

        [JsonIgnore]
        public User? User { get; set; }
    }
}
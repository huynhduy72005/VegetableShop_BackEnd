using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VegetableShopAPI.Models
{
    [Table("KitchenTips")]
    public class KitchenTip
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(220)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Summary { get; set; } // Tóm tắt ngắn hiển thị ngoài thẻ bài viết

        [Required]
        public string Content { get; set; } = string.Empty; // Nội dung chi tiết bài viết

        [MaxLength(500)]
        public string? Image { get; set; } // URL hoặc base64 ảnh bìa

        [Required]
        [MaxLength(80)]
        public string Category { get; set; } = "Mẹo bảo quản";
        // VD: "Mẹo bảo quản" | "Công thức nấu ăn" | "Dinh dưỡng xanh" | "Sơ chế sạch"

        [MaxLength(120)]
        public string AuthorName { get; set; } = "Bếp VEGETABLE";

        public bool IsPublished { get; set; } = true; // true: Hiển thị | false: Bản nháp / Ẩn

        public int ViewCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }
    }
}
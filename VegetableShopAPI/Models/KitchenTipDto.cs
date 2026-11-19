using System.ComponentModel.DataAnnotations;

namespace VegetableShopAPI.Models
{
    public class UpsertKitchenTipDto
    {
        [Required(ErrorMessage = "Tiêu đề bài viết không được để trống.")]
        [MaxLength(220)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Summary { get; set; }

        [Required(ErrorMessage = "Nội dung bài viết không được để trống.")]
        public string Content { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Image { get; set; }

        [MaxLength(80)]
        public string? Category { get; set; }

        [MaxLength(120)]
        public string? AuthorName { get; set; }

        public bool IsPublished { get; set; } = true;
    }
}
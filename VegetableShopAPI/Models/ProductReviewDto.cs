using System.ComponentModel.DataAnnotations;

namespace VegetableShopAPI.Models
{
    public class CreateProductReviewDto
    {
        [Required]
        public int ProductId { get; set; }

        [Range(1, 5, ErrorMessage = "Số sao đánh giá phải từ 1 đến 5.")]
        public int Rating { get; set; } = 5;

        [Required(ErrorMessage = "Vui lòng nhập nội dung đánh giá.")]
        [MaxLength(1000)]
        public string Comment { get; set; } = string.Empty;
    }

    public class UpdateReviewVisibilityDto
    {
        public bool IsHidden { get; set; }
    }

    public class ReplyReviewDto
    {
        [MaxLength(1000)]
        public string? AdminReply { get; set; }
    }
}
namespace VegetableShopAPI.Models
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // Ví dụ: Rau xanh, Củ, Quả, Trái cây...
        public string? Description { get; set; }
    }
}
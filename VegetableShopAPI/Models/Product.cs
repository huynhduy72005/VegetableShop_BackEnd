namespace VegetableShopAPI.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string Unit { get; set; } = string.Empty; // Đơn vị: kg, bó, quả...
        public string? Image { get; set; }
        public string? Description { get; set; }
        public int CategoryId { get; set; }
    }
}
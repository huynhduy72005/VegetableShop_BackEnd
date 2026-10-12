namespace VegetableShopAPI.Models
{
    public class OrderDetail
    {
        public int Id { get; set; }
        public int OrderId { get; set; } // Thuộc đơn hàng nào
        public int ProductId { get; set; } // Sản phẩm nào
        public int Quantity { get; set; } // Số lượng mua
        public decimal Price { get; set; } // Giá tại thời điểm mua
    }
}
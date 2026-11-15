namespace VegetableShopAPI.Models
{
    public class OrderDetail
    {
        public int Id { get; set; }
        public int OrderId { get; set; } // Thuộc đơn hàng nào
        public int ProductId { get; set; } // Sản phẩm nào
        public int Quantity { get; set; } // Số lượng mua
        public decimal Price { get; set; } // Giá tại thời điểm mua

        // Biến thể đã mua. Không đặt khóa ngoại để xóa biến thể không làm hỏng lịch sử đơn.
        public int? VariantId { get; set; }
        public string? VariantName { get; set; } // Lưu tên tại thời điểm mua
    }
}
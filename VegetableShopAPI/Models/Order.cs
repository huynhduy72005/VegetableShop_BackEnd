namespace VegetableShopAPI.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int UserId { get; set; } // Khách hàng nào đặt đơn này
        public DateTime OrderDate { get; set; } = DateTime.Now; // Ngày đặt hàng
        public decimal TotalPrice { get; set; } // Tổng tiền đơn hàng
        public string Status { get; set; } = "Pending"; // Trạng thái: Pending (Chờ xử lý), Completed (Hoàn thành)...
        public string ShippingAddress { get; set; } = string.Empty; // Địa chỉ giao hàng
        public string RecipientName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public decimal? ShippingFee { get; set; }
    }
}
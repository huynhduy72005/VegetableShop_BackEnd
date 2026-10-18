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
        public string RecipientName { get; internal set; }
        public string Phone { get; internal set; }
        public string Note { get; internal set; }
        public string PaymentMethod { get; internal set; }
        public decimal? ShippingFee { get; internal set; }
    }
}
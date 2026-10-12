using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // POST: api/orders (Tạo đơn hàng mới từ giỏ hàng)
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderDto orderDto)
        {
            try
            {
                // 1. Tạo thông tin chung của đơn hàng
                var order = new Order
                {
                    UserId = orderDto.UserId,
                    TotalPrice = orderDto.TotalPrice,
                    ShippingAddress = orderDto.ShippingAddress,
                    OrderDate = DateTime.Now,
                    Status = "Pending" // Trạng thái chờ xử lý
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync(); // Lưu để lấy ID đơn hàng vừa tạo

                // 2. Lưu từng sản phẩm trong giỏ hàng vào bảng OrderDetails
                foreach (var item in orderDto.Items)
                {
                    var orderDetail = new OrderDetail
                    {
                        OrderId = order.Id,
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Price = item.Price
                    };
                    _context.OrderDetails.Add(orderDetail);
                }

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Đặt hàng thành công!", orderId = order.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Lỗi khi đặt hàng: " + ex.Message });
            }
        }

        // GET: api/orders/revenue (Thống kê doanh thu)
        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenueStatistics()
        {
            var totalRevenue = await _context.Orders.SumAsync(o => o.TotalPrice);
            var totalOrders = await _context.Orders.CountAsync();
            var pendingOrders = await _context.Orders.CountAsync(o => o.Status == "Pending");

            return Ok(new
            {
                totalRevenue,
                totalOrders,
                pendingOrders
            });
        }
    }

    // Class phụ trợ (DTO) để nhận dữ liệu JSON từ Frontend gửi lên
    public class OrderDto
    {
        public int UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }



}
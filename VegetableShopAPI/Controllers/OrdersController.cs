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

        // GET: api/orders  (có thể lọc theo khách: api/orders?userId=5)
        [HttpGet]
        public async Task<IActionResult> GetOrders([FromQuery] int? userId)
        {
            var query = _context.Orders.AsNoTracking().AsQueryable();
            if (userId.HasValue)
                query = query.Where(o => o.UserId == userId.Value);

            var orders = await (
                from o in query
                join usr in _context.Users.AsNoTracking() on o.UserId equals usr.Id into userGroup
                from u in userGroup.DefaultIfEmpty()
                orderby o.OrderDate descending, o.Id descending
                select new
                {
                    o.Id,
                    o.UserId,
                    CustomerName = u != null ? u.FullName : "Khách vãng lai",
                    CustomerEmail = u != null ? u.Email : null,
                    o.OrderDate,
                    o.TotalPrice,
                    o.Status,
                    o.ShippingAddress
                }).ToListAsync();

            // Đếm số dòng sản phẩm của từng đơn
            var lineCounts = await _context.OrderDetails.AsNoTracking()
                .GroupBy(d => d.OrderId)
                .Select(g => new { OrderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OrderId, x => x.Count);

            return Ok(orders.Select(o => new
            {
                o.Id,
                o.UserId,
                o.CustomerName,
                o.CustomerEmail,
                o.OrderDate,
                o.TotalPrice,
                o.Status,
                o.ShippingAddress,
                ItemCount = lineCounts.GetValueOrDefault(o.Id)
            }));
        }

        // GET: api/orders/5  (chi tiết một đơn hàng)
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound(new { message = "Không tìm thấy đơn hàng!" });

            var customer = await _context.Users.AsNoTracking()
                .Where(u => u.Id == order.UserId)
                .Select(u => new { u.FullName, u.Email })
                .FirstOrDefaultAsync();

            var items = await (
                from d in _context.OrderDetails.AsNoTracking()
                where d.OrderId == id
                join prod in _context.Products.AsNoTracking() on d.ProductId equals prod.Id into productGroup
                from p in productGroup.DefaultIfEmpty()
                select new
                {
                    d.Id,
                    d.ProductId,
                    ProductName = p != null ? p.Name : null,
                    Unit = p != null ? p.Unit : null,
                    Image = p != null ? p.Image : null,
                    d.Quantity,
                    d.Price,
                    LineTotal = d.Quantity * d.Price
                }).ToListAsync();

            return Ok(new
            {
                order.Id,
                order.UserId,
                CustomerName = customer != null ? customer.FullName : "Khách vãng lai",
                CustomerEmail = customer?.Email,
                order.OrderDate,
                order.TotalPrice,
                order.Status,
                order.ShippingAddress,
                Items = items
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
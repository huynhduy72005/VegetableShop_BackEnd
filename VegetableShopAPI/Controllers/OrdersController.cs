using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net;
using System.Net.Mail;
using System.Text;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<OrdersController> _logger;
        private readonly IConfiguration _configuration;
        private readonly EmployeePermissionService _permissions;
        private static readonly string[] AllowedOrderStatuses =
        {
            "Pending",
            "Processing",
            "Shipping",
            "Completed",
            "Cancelled"
        };

        public OrdersController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<OrdersController> logger,
            IConfiguration configuration,
            EmployeePermissionService permissions)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
            _configuration = configuration;
            _permissions = permissions;
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
                    RecipientName = orderDto.RecipientName?.Trim() ?? string.Empty,
                    Phone = orderDto.Phone?.Trim() ?? string.Empty,
                    Note = orderDto.Note?.Trim() ?? string.Empty,
                    PaymentMethod = orderDto.PaymentMethod?.Trim() ?? string.Empty,
                    ShippingFee = orderDto.ShippingFee,
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

                // 3. Gửi email xác nhận (lỗi gửi mail KHÔNG làm hỏng đơn hàng)
                var emailSent = await TrySendOrderConfirmationEmailAsync(order, orderDto);

                // 4. Báo cho chủ shop biết có đơn mới (gửi về Gmail trong EmailSettings)
                await TrySendShopNotificationEmailAsync(order, orderDto);

                return Ok(new
                {
                    success = true,
                    message = "Đặt hàng thành công!",
                    orderId = order.Id,
                    emailSent
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo đơn hàng");
                // EF Core hay bọc lỗi thật (vd: Invalid column name) trong InnerException
                var detail = ex.InnerException?.Message ?? ex.Message;
                return BadRequest(new { success = false, message = "Lỗi khi đặt hàng: " + detail });
            }
        }

        // GET: api/orders/revenue (Thống kê doanh thu)
        [HttpGet("revenue")]
        [Authorize]
        public async Task<IActionResult> GetRevenueStatistics()
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

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
        [Authorize]
        public async Task<IActionResult> GetOrders([FromQuery] int? userId)
        {
            var canManageOrders = await _permissions.CanManageOrdersAsync(User);
            if (!canManageOrders)
            {
                if (!int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var currentUserId))
                    return Unauthorized();

                if (userId.HasValue && userId.Value != currentUserId)
                    return Forbid();

                userId = currentUserId;
            }

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
        [Authorize]
        public async Task<IActionResult> GetOrder(int id)
        {
            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound(new { message = "Không tìm thấy đơn hàng!" });

            if (!await _permissions.CanManageOrdersAsync(User) &&
                (!int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var currentUserId) ||
                 order.UserId != currentUserId))
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
                    StockQuantity = p != null ? p.Quantity : 0,
                    d.Quantity,
                    d.Price,
                    LineTotal = d.Quantity * d.Price
                }).ToListAsync();

            var (shippingAddress, legacyPhone, legacyNote) = ParseLegacyShippingAddress(order.ShippingAddress);
            var shippingFee = order.ShippingFee ?? Math.Max(
                0,
                order.TotalPrice - items.Sum(item => item.LineTotal));

            return Ok(new
            {
                order.Id,
                order.UserId,
                CustomerName = !string.IsNullOrWhiteSpace(order.RecipientName)
                    ? order.RecipientName
                    : customer != null ? customer.FullName : "Khách vãng lai",
                CustomerEmail = customer?.Email,
                order.RecipientName,
                Phone = !string.IsNullOrWhiteSpace(order.Phone) ? order.Phone : legacyPhone,
                Note = !string.IsNullOrWhiteSpace(order.Note) ? order.Note : legacyNote,
                order.PaymentMethod,
                order.OrderDate,
                order.TotalPrice,
                ShippingFee = shippingFee,
                order.Status,
                ShippingAddress = shippingAddress,
                Items = items
            });
        }

        private static (string Address, string Phone, string Note) ParseLegacyShippingAddress(string? shippingAddress)
        {
            const string phoneMarker = " - SĐT: ";
            const string noteMarker = " - Ghi chú: ";
            var value = shippingAddress ?? string.Empty;
            var phoneIndex = value.IndexOf(phoneMarker, StringComparison.OrdinalIgnoreCase);
            var noteIndex = value.IndexOf(noteMarker, StringComparison.OrdinalIgnoreCase);
            var addressEnd = new[] { phoneIndex, noteIndex }
                .Where(index => index >= 0)
                .DefaultIfEmpty(value.Length)
                .Min();

            var address = value[..addressEnd].Trim();
            var phone = phoneIndex >= 0
                ? value[(phoneIndex + phoneMarker.Length)..(noteIndex > phoneIndex ? noteIndex : value.Length)].Trim()
                : string.Empty;
            var note = noteIndex >= 0
                ? value[(noteIndex + noteMarker.Length)..].Trim()
                : string.Empty;

            return (address, phone, note);
        }

        // PUT: api/orders/5/status
        [HttpPut("{id:int}/status")]
        [Authorize]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusDto request)
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            if (string.IsNullOrWhiteSpace(request.Status))
                return BadRequest(new { message = "Trạng thái đơn hàng không được để trống." });

            var status = AllowedOrderStatuses.FirstOrDefault(
                allowed => string.Equals(allowed, request.Status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (status == null)
                return BadRequest(new { message = "Trạng thái đơn hàng không hợp lệ." });

            var order = await _context.Orders.FindAsync(id);
            if (order == null)
                return NotFound(new { message = "Không tìm thấy đơn hàng!" });

            order.Status = status;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, order.Id, order.Status });
        }

        // PUT: api/orders/5/cancel?userId=1
        [HttpPut("{id:int}/cancel")]
        [Authorize]
        public async Task<IActionResult> CancelOrder(int id, [FromQuery] int userId)
        {
            if (!int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var currentUserId))
                return Unauthorized();

            if (userId != currentUserId)
                return Forbid();

            var order = await _context.Orders.FirstOrDefaultAsync(
                item => item.Id == id && item.UserId == userId);
            if (order == null)
                return NotFound(new { message = "Không tìm thấy đơn hàng!" });

            if (!string.Equals(order.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = "Chỉ có thể hủy đơn hàng đang chờ xác nhận." });

            order.Status = "Cancelled";
            await _context.SaveChangesAsync();

            return Ok(new { success = true, order.Id, order.Status });
        }

        // ===================== GỬI EMAIL XÁC NHẬN ĐƠN HÀNG =====================

        private async Task<bool> TrySendOrderConfirmationEmailAsync(Order order, OrderDto orderDto)
        {
            try
            {
                var toEmail = orderDto.Email?.Trim();
                if (string.IsNullOrWhiteSpace(toEmail) || !IsValidEmail(toEmail))
                {
                    _logger.LogWarning(
                        "Đơn #{OrderId}: không có email hợp lệ nên không gửi mail xác nhận.", order.Id);
                    return false;
                }

                // Lấy tên sản phẩm để hiển thị trong mail
                var productIds = orderDto.Items.Select(i => i.ProductId).Distinct().ToList();
                var productNames = await _context.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

                var subject = $"Xác nhận đơn hàng #{order.Id} - Vườn Nhà";
                var body = BuildOrderEmailBody(order, orderDto, productNames);

                // Chỉ chờ tối đa 10 giây để khách không bị treo màn hình nếu SMTP chậm/bị chặn
                var sendTask = _emailService.SendEmailAsync(toEmail, subject, body);
                var finished = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(10)));

                if (finished != sendTask)
                {
                    _logger.LogWarning("Đơn #{OrderId}: gửi email quá 10 giây, bỏ qua chờ.", order.Id);
                    _ = sendTask.ContinueWith(
                        t => _logger.LogError(t.Exception, "Đơn #{OrderId}: gửi email thất bại (sau timeout)", order.Id),
                        TaskContinuationOptions.OnlyOnFaulted);
                    return false;
                }

                await sendTask; // nếu gửi lỗi thì ném exception ở đây để catch bên dưới ghi log
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không gửi được email xác nhận cho đơn #{OrderId}", order.Id);
                return false;
            }
        }

        // Gửi thông báo "có đơn hàng mới" về Gmail của shop.
        // Ưu tiên EmailSettings:AdminNotifyEmail (nếu bạn tự thêm), không có thì dùng EmailSettings:SenderEmail.
        private async Task TrySendShopNotificationEmailAsync(Order order, OrderDto orderDto)
        {
            try
            {
                var shopEmail = _configuration["EmailSettings:AdminNotifyEmail"];
                if (string.IsNullOrWhiteSpace(shopEmail))
                    shopEmail = _configuration["EmailSettings:SenderEmail"];

                if (string.IsNullOrWhiteSpace(shopEmail) || !IsValidEmail(shopEmail.Trim()))
                {
                    _logger.LogWarning("Đơn #{OrderId}: chưa cấu hình email của shop nên không gửi thông báo.", order.Id);
                    return;
                }

                var productIds = orderDto.Items.Select(i => i.ProductId).Distinct().ToList();
                var productNames = await _context.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

                var customerEmail = string.IsNullOrWhiteSpace(orderDto.Email) ? "(không nhập)" : orderDto.Email.Trim();
                var header = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;padding:12px;background:#fff8e1;border-radius:8px;margin-bottom:12px;'>
    🔔 <b>Có đơn hàng mới #{order.Id}</b><br/>
    Khách: {WebUtility.HtmlEncode(order.RecipientName)} · SĐT: {WebUtility.HtmlEncode(order.Phone)}<br/>
    Email khách: {WebUtility.HtmlEncode(customerEmail)}
</div>";

                var subject = $"[Đơn mới] #{order.Id} - {order.TotalPrice:N0} đ";
                var body = header + BuildOrderEmailBody(order, orderDto, productNames);

                var sendTask = _emailService.SendEmailAsync(shopEmail.Trim(), subject, body);
                var finished = await Task.WhenAny(sendTask, Task.Delay(TimeSpan.FromSeconds(10)));

                if (finished != sendTask)
                {
                    _logger.LogWarning("Đơn #{OrderId}: gửi thông báo cho shop quá 10 giây, bỏ qua chờ.", order.Id);
                    _ = sendTask.ContinueWith(
                        t => _logger.LogError(t.Exception, "Đơn #{OrderId}: gửi thông báo cho shop thất bại (sau timeout)", order.Id),
                        TaskContinuationOptions.OnlyOnFaulted);
                    return;
                }

                await sendTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không gửi được thông báo đơn mới cho shop (đơn #{OrderId})", order.Id);
            }
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var address = new MailAddress(email);
                return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildOrderEmailBody(
            Order order,
            OrderDto orderDto,
            Dictionary<int, string> productNames)
        {
            static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
            static string Money(decimal value) => $"{value:N0} đ";

            var subtotal = orderDto.Items.Sum(i => i.Price * i.Quantity);
            var shippingFee = order.ShippingFee ?? 0;
            var paymentText = string.Equals(order.PaymentMethod, "COD", StringComparison.OrdinalIgnoreCase)
                ? "Thanh toán khi nhận hàng (COD)"
                : "Chuyển khoản ngân hàng";

            var rows = new StringBuilder();
            foreach (var item in orderDto.Items)
            {
                var name = productNames.TryGetValue(item.ProductId, out var productName)
                    ? productName
                    : $"Sản phẩm #{item.ProductId}";

                rows.Append($@"
                <tr>
                    <td style='padding:8px;border-bottom:1px solid #eee;'>{Encode(name)}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:center;'>{item.Quantity}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:right;'>{Money(item.Price)}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:right;'>{Money(item.Price * item.Quantity)}</td>
                </tr>");
            }

            var noteHtml = string.IsNullOrWhiteSpace(order.Note)
                ? string.Empty
                : $"<p><b>Ghi chú:</b> {Encode(order.Note)}</p>";

            return $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;color:#333;'>
    <h2 style='color:#2e7d32;'>Cảm ơn {Encode(order.RecipientName)} đã đặt hàng tại Vườn Nhà 🥬</h2>
    <p>Đơn hàng của bạn đã được ghi nhận và đang chờ xử lý.</p>

    <p><b>Mã đơn hàng:</b> #{order.Id}<br/>
       <b>Ngày đặt:</b> {order.OrderDate:dd/MM/yyyy HH:mm}</p>

    <h3 style='margin-bottom:4px;'>Thông tin nhận hàng</h3>
    <p style='margin-top:0;'>
        <b>Người nhận:</b> {Encode(order.RecipientName)}<br/>
        <b>Số điện thoại:</b> {Encode(order.Phone)}<br/>
        <b>Địa chỉ:</b> {Encode(order.ShippingAddress)}<br/>
        <b>Thanh toán:</b> {paymentText}
    </p>
    {noteHtml}

    <h3 style='margin-bottom:4px;'>Chi tiết đơn hàng</h3>
    <table style='width:100%;border-collapse:collapse;'>
        <thead>
            <tr style='background:#f1f8e9;'>
                <th style='padding:8px;text-align:left;'>Sản phẩm</th>
                <th style='padding:8px;text-align:center;'>SL</th>
                <th style='padding:8px;text-align:right;'>Đơn giá</th>
                <th style='padding:8px;text-align:right;'>Thành tiền</th>
            </tr>
        </thead>
        <tbody>{rows}</tbody>
    </table>

    <p style='text-align:right;'>
        Tạm tính: {Money(subtotal)}<br/>
        Phí giao hàng: {Money(shippingFee)}<br/>
        <b style='font-size:16px;color:#2e7d32;'>Tổng thanh toán: {Money(order.TotalPrice)}</b>
    </p>

    <p>Chúng tôi sẽ liên hệ và giao hàng sớm nhất có thể. Cảm ơn bạn đã ủng hộ!</p>
</div>";
        }
    }

    // Class phụ trợ (DTO) để nhận dữ liệu JSON từ Frontend gửi lên
    public class OrderDto
    {
        public int UserId { get; set; }
        public decimal TotalPrice { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
        public string? PaymentMethod { get; set; }
        public decimal? ShippingFee { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class UpdateOrderStatusDto
    {
        public string? Status { get; set; }
    }
}
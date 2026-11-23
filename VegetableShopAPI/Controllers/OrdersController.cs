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

        // POST: api/orders (Tạo đơn hàng mới từ giỏ hàng + trừ kho FEFO)
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] OrderDto orderDto)
        {
            try
            {
                var variantIds = orderDto.Items
                    .Where(i => i.VariantId.HasValue)
                    .Select(i => i.VariantId!.Value)
                    .Distinct()
                    .ToList();
                var variantMap = await _context.ProductVariants.AsNoTracking()
                    .Where(v => variantIds.Contains(v.Id))
                    .ToDictionaryAsync(v => v.Id);

                var orderedProductIds = orderDto.Items.Select(i => i.ProductId).Distinct().ToList();
                var productMap = await _context.Products.AsNoTracking()
                    .Where(p => orderedProductIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id);

                var productsWithVariants = (await _context.ProductVariants.AsNoTracking()
                    .Where(v => orderedProductIds.Contains(v.ProductId))
                    .Select(v => v.ProductId)
                    .Distinct()
                    .ToListAsync()).ToHashSet();

                // Kiểm tra tồn kho khả dụng (ưu tiên tính từ các lô CÒN HẠN nếu sản phẩm có quản lý lô)
                var today = DateTime.Today;
                var allBatchesForOrderedProducts = await _context.ProductBatches.AsNoTracking()
                    .Where(b => orderedProductIds.Contains(b.ProductId))
                    .ToListAsync();

                foreach (var item in orderDto.Items)
                {
                    if (!productMap.TryGetValue(item.ProductId, out var product) || !product.IsVisible)
                    {
                        return BadRequest(new { success = false, message = "Có sản phẩm không tồn tại hoặc đã ngừng bán." });
                    }

                    if (!item.VariantId.HasValue)
                        item.VariantName = null;

                    if (item.VariantId.HasValue)
                    {
                        if (!variantMap.TryGetValue(item.VariantId.Value, out var variant) ||
                            variant.ProductId != item.ProductId ||
                            !variant.IsVisible)
                        {
                            return BadRequest(new { success = false, message = "Loại sản phẩm không hợp lệ, đã ngừng bán hoặc đã bị xóa. Vui lòng chọn lại." });
                        }

                        var variantBatches = allBatchesForOrderedProducts
                            .Where(b => b.ProductId == item.ProductId && b.ProductVariantId == item.VariantId.Value)
                            .ToList();

                        var availableStock = variantBatches.Count > 0
                            ? variantBatches.Where(b => b.ExpiryDate.Date >= today).Sum(b => b.RemainingQuantity)
                            : variant.Quantity;

                        if (availableStock < item.Quantity)
                        {
                            return BadRequest(new { success = false, message = $"Loại “{variant.Name}” chỉ còn {availableStock} sản phẩm khả dụng." });
                        }

                        item.Price = variant.Price;
                        item.VariantName = variant.Name;
                    }
                    else if (productsWithVariants.Contains(item.ProductId))
                    {
                        return BadRequest(new { success = false, message = "Có sản phẩm gồm nhiều loại, vui lòng chọn loại cụ thể trong trang chi tiết trước khi đặt hàng." });
                    }
                    else
                    {
                        var productBatches = allBatchesForOrderedProducts
                            .Where(b => b.ProductId == item.ProductId && b.ProductVariantId == null)
                            .ToList();

                        var availableStock = productBatches.Count > 0
                            ? productBatches.Where(b => b.ExpiryDate.Date >= today).Sum(b => b.RemainingQuantity)
                            : product.Quantity;

                        if (availableStock < item.Quantity)
                        {
                            return BadRequest(new { success = false, message = $"Sản phẩm “{product.Name}” chỉ còn {availableStock} {product.Unit} khả dụng." });
                        }
                    }
                }
                var hasVariantLines = variantMap.Count > 0;

                // 0. Kiểm tra voucher (nếu khách chọn) - tiền giảm luôn do SERVER tính
                UserVoucher? userVoucher = null;
                decimal discount = 0;
                string? voucherCode = null;
                var subtotal = orderDto.Items.Sum(i => i.Price * i.Quantity);

                if (!string.IsNullOrWhiteSpace(orderDto.VoucherCode))
                {
                    if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var voucherUserId))
                        return BadRequest(new { success = false, message = "Vui lòng đăng nhập để dùng voucher." });

                    var code = orderDto.VoucherCode.Trim().ToUpperInvariant();
                    userVoucher = await _context.UserVouchers
                        .Include(x => x.Voucher)
                        .FirstOrDefaultAsync(x => x.UserId == voucherUserId &&
                                                  x.UsedAt == null &&
                                                  x.Voucher!.Code == code);

                    if (userVoucher?.Voucher == null)
                        return BadRequest(new { success = false, message = "Voucher không có trong ví hoặc đã được sử dụng." });

                    var evaluation = VoucherRules.Evaluate(userVoucher.Voucher, subtotal, DateTime.Now);
                    if (!evaluation.Ok)
                        return BadRequest(new { success = false, message = evaluation.Message });

                    discount = evaluation.Discount;
                    voucherCode = userVoucher.Voucher.Code;
                }

                // 1. Tạo thông tin chung của đơn hàng
                var order = new Order
                {
                    UserId = orderDto.UserId,
                    TotalPrice = (voucherCode != null || hasVariantLines)
                        ? Math.Max(0, subtotal + (orderDto.ShippingFee ?? 0) - discount)
                        : orderDto.TotalPrice,
                    VoucherCode = voucherCode,
                    DiscountAmount = voucherCode != null ? discount : null,
                    ShippingAddress = orderDto.ShippingAddress,
                    RecipientName = orderDto.RecipientName?.Trim() ?? string.Empty,
                    Phone = orderDto.Phone?.Trim() ?? string.Empty,
                    Note = orderDto.Note?.Trim() ?? string.Empty,
                    PaymentMethod = orderDto.PaymentMethod?.Trim() ?? string.Empty,
                    ShippingFee = orderDto.ShippingFee,
                    OrderDate = DateTime.Now,
                    Status = "Pending"
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                // 2. Lưu chi tiết đơn hàng & Trừ tồn kho theo FEFO
                foreach (var item in orderDto.Items)
                {
                    var orderDetail = new OrderDetail
                    {
                        OrderId = order.Id,
                        ProductId = item.ProductId,
                        ProductVariantId = item.VariantId,
                        VariantName = item.VariantName,
                        Quantity = item.Quantity,
                        Price = item.Price
                    };
                    _context.OrderDetails.Add(orderDetail);

                    await DeductStockFefoAsync(item.ProductId, item.VariantId, item.Quantity);
                }

                // Đánh dấu voucher đã dùng
                if (userVoucher != null)
                {
                    userVoucher.UsedAt = DateTime.Now;
                    userVoucher.OrderId = order.Id;
                }

                await _context.SaveChangesAsync();

                // 3. Gửi email xác nhận
                var emailSent = await TrySendOrderConfirmationEmailAsync(order, orderDto);

                // 4. Báo cho chủ shop biết có đơn mới
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
                var detail = ex.InnerException?.Message ?? ex.Message;
                return BadRequest(new { success = false, message = "Lỗi khi đặt hàng: " + detail });
            }
        }

       
        // GET: api/orders/pending-summary (Kiểm tra nhanh đơn chờ xác nhận cho chuông báo Real-time)
        [HttpGet("pending-summary")]
        [Authorize]
        public async Task<IActionResult> GetPendingSummary()
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var pendingOrders = await (
                from o in _context.Orders.AsNoTracking()
                where o.Status == "Pending"
                join usr in _context.Users.AsNoTracking() on o.UserId equals usr.Id into userGroup
                from u in userGroup.DefaultIfEmpty()
                orderby o.Id descending
                select new
                {
                    o.Id,
                    CustomerName = !string.IsNullOrWhiteSpace(o.RecipientName)
                        ? o.RecipientName
                        : (u != null ? u.FullName : "Khách vãng lai"),
                    o.TotalPrice,
                    o.OrderDate
                }
            ).ToListAsync();

            var latest = pendingOrders.FirstOrDefault();
            object? latestOrderPayload = null;

            if (latest != null)
            {
                var firstItem = await (
                    from d in _context.OrderDetails.AsNoTracking()
                    where d.OrderId == latest.Id
                    orderby d.Id ascending
                    join prod in _context.Products.AsNoTracking() on d.ProductId equals prod.Id into productGroup
                    from p in productGroup.DefaultIfEmpty()
                    join variant in _context.ProductVariants.AsNoTracking() on d.ProductVariantId equals (int?)variant.Id into variantGroup
                    from v in variantGroup.DefaultIfEmpty()
                    select new
                    {
                        ProductName = p != null ? p.Name : null,
                        d.VariantName,
                        ProductImage = d.ProductVariantId != null && v != null && v.Image != null && v.Image != ""
                            ? v.Image
                            : (p != null ? p.Image : null)
                    }
                ).FirstOrDefaultAsync();

                var itemCount = await _context.OrderDetails.AsNoTracking()
                    .CountAsync(d => d.OrderId == latest.Id);

                latestOrderPayload = new
                {
                    latest.Id,
                    latest.CustomerName,
                    latest.TotalPrice,
                    latest.OrderDate,
                    ProductName = firstItem?.ProductName,
                    VariantName = firstItem?.VariantName,
                    ProductImage = firstItem?.ProductImage,
                    ItemCount = itemCount
                };
            }

            return Ok(new
            {
                pendingCount = pendingOrders.Count,
                latestOrderId = latest?.Id ?? 0,
                latestOrder = latestOrderPayload
            });
        }
        // GET: api/orders/revenue (Thống kê tổng quát cho thẻ đầu trang Tổng quan)
        [HttpGet("revenue")]
        [Authorize]
        public async Task<IActionResult> GetRevenueStatistics()
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var totalRevenue = await _context.Orders.SumAsync(o => o.TotalPrice);
            var netRevenue = await _context.Orders
                .Where(o => o.Status != "Cancelled")
                .SumAsync(o => o.TotalPrice);
            var completedRevenue = await _context.Orders
                .Where(o => o.Status == "Completed")
                .SumAsync(o => o.TotalPrice);

            var totalOrders = await _context.Orders.CountAsync();
            var pendingOrders = await _context.Orders.CountAsync(o => o.Status == "Pending");
            var completedOrders = await _context.Orders.CountAsync(o => o.Status == "Completed");
            var cancelledOrders = await _context.Orders.CountAsync(o => o.Status == "Cancelled");

            return Ok(new
            {
                totalRevenue,
                netRevenue,
                completedRevenue,
                totalOrders,
                pendingOrders,
                completedOrders,
                cancelledOrders
            });
        }

        // GET: api/orders
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

        // GET: api/orders/5
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
                join variant in _context.ProductVariants.AsNoTracking() on d.ProductVariantId equals (int?)variant.Id into variantGroup
                from v in variantGroup.DefaultIfEmpty()
                select new
                {
                    d.Id,
                    d.ProductId,
                    VariantId = d.ProductVariantId,
                    d.VariantName,
                    ProductName = p != null ? p.Name : null,
                    Unit = d.ProductVariantId != null && v != null && v.Unit != ""
                        ? v.Unit
                        : (p != null ? p.Unit : null),
                    Image = d.ProductVariantId != null && v != null && v.Image != null && v.Image != ""
                        ? v.Image
                        : (p != null ? p.Image : null),
                    StockQuantity = d.ProductVariantId != null
                        ? (v != null && v.IsVisible ? v.Quantity : 0)
                        : (p != null ? p.Quantity : 0),
                    CurrentPrice = d.ProductVariantId != null
                        ? (v != null ? v.Price : d.Price)
                        : d.Price,
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
                order.VoucherCode,
                DiscountAmount = order.DiscountAmount ?? 0,
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

            var wasCancelled = string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
            order.Status = status;

            if (status == "Cancelled" && !wasCancelled)
            {
                await ReleaseVoucherAsync(order.Id);
                await RestoreStockOnCancelAsync(order.Id);
            }

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
            await ReleaseVoucherAsync(order.Id);
            await RestoreStockOnCancelAsync(order.Id);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, order.Id, order.Status });
        }

        // ===================== THUẬT TOÁN TRỪ KHO FEFO & HOÀN KHO =====================

        private async Task DeductStockFefoAsync(int productId, int? variantId, int quantityToDeduct)
        {
            var today = DateTime.Today;
            var hasBatches = await _context.ProductBatches
                .AnyAsync(b => b.ProductId == productId && b.ProductVariantId == variantId);

            if (hasBatches)
            {
                var availableBatches = await _context.ProductBatches
                    .Where(b => b.ProductId == productId &&
                                b.ProductVariantId == variantId &&
                                b.RemainingQuantity > 0 &&
                                b.ExpiryDate.Date >= today)
                    .OrderBy(b => b.ExpiryDate)
                    .ThenBy(b => b.ImportDate)
                    .ThenBy(b => b.Id)
                    .ToListAsync();

                var remaining = quantityToDeduct;
                foreach (var batch in availableBatches)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(batch.RemainingQuantity, remaining);
                    batch.RemainingQuantity -= take;
                    remaining -= take;
                }

                await _context.SaveChangesAsync();
                await ProductBatchesController.RecalculateProductStockAsync(_context, productId);
            }
            else
            {
                var product = await _context.Products
                    .Include(p => p.Variants)
                    .FirstOrDefaultAsync(p => p.Id == productId);

                if (product == null) return;

                if (variantId.HasValue && product.Variants != null)
                {
                    var variant = product.Variants.FirstOrDefault(v => v.Id == variantId.Value);
                    if (variant != null)
                        variant.Quantity = Math.Max(0, variant.Quantity - quantityToDeduct);

                    product.Quantity = product.Variants.Where(v => v.IsVisible).Sum(v => v.Quantity);
                }
                else
                {
                    product.Quantity = Math.Max(0, product.Quantity - quantityToDeduct);
                }
            }
        }

        private async Task RestoreStockOnCancelAsync(int orderId)
        {
            var details = await _context.OrderDetails
                .Where(d => d.OrderId == orderId)
                .ToListAsync();

            var today = DateTime.Today;
            foreach (var d in details)
            {
                var latestValidBatch = await _context.ProductBatches
                    .Where(b => b.ProductId == d.ProductId &&
                                b.ProductVariantId == d.ProductVariantId &&
                                b.ExpiryDate.Date >= today)
                    .OrderByDescending(b => b.ExpiryDate)
                    .FirstOrDefaultAsync();

                if (latestValidBatch != null)
                {
                    latestValidBatch.RemainingQuantity += d.Quantity;
                    await _context.SaveChangesAsync();
                    await ProductBatchesController.RecalculateProductStockAsync(_context, d.ProductId);
                }
                else
                {
                    var hasAnyBatches = await _context.ProductBatches.AnyAsync(b => b.ProductId == d.ProductId);
                    if (!hasAnyBatches)
                    {
                        var product = await _context.Products
                            .Include(p => p.Variants)
                            .FirstOrDefaultAsync(p => p.Id == d.ProductId);
                        if (product == null) continue;

                        if (d.ProductVariantId.HasValue && product.Variants != null)
                        {
                            var v = product.Variants.FirstOrDefault(x => x.Id == d.ProductVariantId.Value);
                            if (v != null) v.Quantity += d.Quantity;
                            product.Quantity = product.Variants.Where(x => x.IsVisible).Sum(x => x.Quantity);
                        }
                        else
                        {
                            product.Quantity += d.Quantity;
                        }
                    }
                }
            }
        }

        private async Task ReleaseVoucherAsync(int orderId)
        {
            var used = await _context.UserVouchers.FirstOrDefaultAsync(x => x.OrderId == orderId);
            if (used != null)
            {
                used.UsedAt = null;
                used.OrderId = null;
            }
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

                var productIds = orderDto.Items.Select(i => i.ProductId).Distinct().ToList();
                var productNames = await _context.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

                var subject = $"Xác nhận đơn hàng #{order.Id} - VEGETABLE";
                var body = BuildOrderEmailBody(order, orderDto, productNames);

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

                await sendTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không gửi được email xác nhận cho đơn #{OrderId}", order.Id);
                return false;
            }
        }

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
                if (!string.IsNullOrWhiteSpace(item.VariantName))
                    name += $" ({item.VariantName})";

                rows.Append($@"
                <tr>
                    <td style='padding:8px;border-bottom:1px solid #eee;'>{Encode(name)}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:center;'>{item.Quantity}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:right;'>{Money(item.Price)}</td>
                    <td style='padding:8px;border-bottom:1px solid #eee;text-align:right;'>{Money(item.Price * item.Quantity)}</td>
                </tr>");
            }

            var discountHtml = (order.DiscountAmount ?? 0) > 0
                ? $"Giảm giá ({Encode(order.VoucherCode)}): -{Money(order.DiscountAmount ?? 0)}<br/>"
                : string.Empty;

            var noteHtml = string.IsNullOrWhiteSpace(order.Note)
                ? string.Empty
                : $"<p><b>Ghi chú:</b> {Encode(order.Note)}</p>";

            return $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto;color:#333;'>
    <h2 style='color:#2e7d32;'>Cảm ơn {Encode(order.RecipientName)} đã đặt hàng tại VEGETABLE 🥬</h2>
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
        {discountHtml}<b style='font-size:16px;color:#2e7d32;'>Tổng thanh toán: {Money(order.TotalPrice)}</b>
    </p>

    <p>Chúng tôi sẽ liên hệ và giao hàng sớm nhất có thể. Cảm ơn bạn đã ủng hộ!</p>
</div>";
        }
    }

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
        public string? VoucherCode { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int? VariantId { get; set; }
        public string? VariantName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class UpdateOrderStatusDto
    {
        public string? Status { get; set; }
    }
}
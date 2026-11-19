using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public AnalyticsController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        // 1. GET: api/Analytics/revenue?mode=day&days=14  HOẶC  ?mode=month&year=2026
        [HttpGet("revenue")]
        public async Task<IActionResult> GetRevenueAnalytics(
            [FromQuery] string mode = "day",
            [FromQuery] int days = 14,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? year = null)
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var normalizedMode = string.Equals(mode, "month", StringComparison.OrdinalIgnoreCase)
                ? "month"
                : "day";

            if (normalizedMode == "month")
            {
                var targetYear = year ?? DateTime.Today.Year;
                var startOfYear = new DateTime(targetYear, 1, 1);
                var endOfYearExclusive = startOfYear.AddYears(1);

                var ordersInYear = await _context.Orders
                    .AsNoTracking()
                    .Where(o => o.OrderDate >= startOfYear && o.OrderDate < endOfYearExclusive)
                    .Select(o => new { o.OrderDate, o.TotalPrice, o.Status })
                    .ToListAsync();

                var points = new List<RevenuePointDto>();
                for (int m = 1; m <= 12; m++)
                {
                    var monthOrders = ordersInYear.Where(o => o.OrderDate.Month == m).ToList();
                    var validOrders = monthOrders
                        .Where(o => !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var completedOrders = monthOrders
                        .Where(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var cancelledCount = monthOrders
                        .Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

                    points.Add(new RevenuePointDto
                    {
                        Key = $"{targetYear}-{m:D2}",
                        Label = $"Thg {m}",
                        FullDate = $"Tháng {m:D2}/{targetYear}",
                        Revenue = validOrders.Sum(o => o.TotalPrice),
                        CompletedRevenue = completedOrders.Sum(o => o.TotalPrice),
                        TotalOrders = monthOrders.Count,
                        CompletedOrders = completedOrders.Count,
                        CancelledOrders = cancelledCount
                    });
                }

                var validAll = ordersInYear
                    .Where(o => !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return Ok(new RevenueSeriesResponseDto
                {
                    Mode = "month",
                    FromDate = startOfYear.ToString("yyyy-MM-dd"),
                    ToDate = new DateTime(targetYear, 12, 31).ToString("yyyy-MM-dd"),
                    Year = targetYear,
                    TotalGrossRevenue = ordersInYear.Sum(o => o.TotalPrice),
                    TotalNetRevenue = validAll.Sum(o => o.TotalPrice),
                    CompletedRevenue = ordersInYear
                        .Where(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                        .Sum(o => o.TotalPrice),
                    TotalOrders = ordersInYear.Count,
                    CompletedOrders = ordersInYear.Count(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
                    CancelledOrders = ordersInYear.Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
                    AverageOrderValue = validAll.Count > 0 ? Math.Round(validAll.Sum(o => o.TotalPrice) / validAll.Count, 0) : 0,
                    Points = points
                });
            }
            else
            {
                var safeDays = Math.Clamp(days, 1, 365);
                var endDay = (toDate ?? DateTime.Today).Date;
                var startDay = (fromDate ?? endDay.AddDays(-(safeDays - 1))).Date;
                if (startDay > endDay)
                    (startDay, endDay) = (endDay, startDay);

                var endExclusive = endDay.AddDays(1);

                var ordersInRange = await _context.Orders
                    .AsNoTracking()
                    .Where(o => o.OrderDate >= startDay && o.OrderDate < endExclusive)
                    .Select(o => new { o.OrderDate, o.TotalPrice, o.Status })
                    .ToListAsync();

                var groupedByDate = ordersInRange
                    .GroupBy(o => o.OrderDate.Date)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var points = new List<RevenuePointDto>();
                for (var d = startDay; d <= endDay; d = d.AddDays(1))
                {
                    groupedByDate.TryGetValue(d, out var dayOrders);
                    dayOrders ??= new();

                    var validOrders = dayOrders
                        .Where(o => !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var completedOrders = dayOrders
                        .Where(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var cancelledCount = dayOrders
                        .Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

                    points.Add(new RevenuePointDto
                    {
                        Key = d.ToString("yyyy-MM-dd"),
                        Label = d.ToString("dd/MM"),
                        FullDate = d.ToString("dd/MM/yyyy"),
                        Revenue = validOrders.Sum(o => o.TotalPrice),
                        CompletedRevenue = completedOrders.Sum(o => o.TotalPrice),
                        TotalOrders = dayOrders.Count,
                        CompletedOrders = completedOrders.Count,
                        CancelledOrders = cancelledCount
                    });
                }

                var validAll = ordersInRange
                    .Where(o => !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return Ok(new RevenueSeriesResponseDto
                {
                    Mode = "day",
                    FromDate = startDay.ToString("yyyy-MM-dd"),
                    ToDate = endDay.ToString("yyyy-MM-dd"),
                    Year = endDay.Year,
                    TotalGrossRevenue = ordersInRange.Sum(o => o.TotalPrice),
                    TotalNetRevenue = validAll.Sum(o => o.TotalPrice),
                    CompletedRevenue = ordersInRange
                        .Where(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                        .Sum(o => o.TotalPrice),
                    TotalOrders = ordersInRange.Count,
                    CompletedOrders = ordersInRange.Count(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
                    CancelledOrders = ordersInRange.Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
                    AverageOrderValue = validAll.Count > 0 ? Math.Round(validAll.Sum(o => o.TotalPrice) / validAll.Count, 0) : 0,
                    Points = points
                });
            }
        }

        // 2. GET: api/Analytics/top-products?top=5&days=30
        [HttpGet("top-products")]
        public async Task<IActionResult> GetTopSellingProducts(
            [FromQuery] int top = 5,
            [FromQuery] int days = 30,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var (startDay, endExclusive) = ResolveDateRange(days, fromDate, toDate);
            var safeTop = Math.Clamp(top, 1, 50);

            var validOrderIds = await _context.Orders
                .AsNoTracking()
                .Where(o => o.OrderDate >= startDay &&
                            o.OrderDate < endExclusive &&
                            o.Status != "Cancelled")
                .Select(o => o.Id)
                .ToListAsync();

            if (validOrderIds.Count == 0)
                return Ok(new List<TopProductReportDto>());

            var orderLines = await _context.OrderDetails
                .AsNoTracking()
                .Where(d => validOrderIds.Contains(d.OrderId))
                .Select(d => new { d.ProductId, d.OrderId, d.Quantity, d.Price })
                .ToListAsync();

            var products = await _context.Products
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id);

            var categories = await _context.Categories
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name);

            var result = orderLines
                .GroupBy(d => d.ProductId)
                .Select(g =>
                {
                    products.TryGetValue(g.Key, out var prod);
                    var catName = prod != null && categories.TryGetValue(prod.CategoryId, out var cName)
                        ? cName
                        : "Chưa phân loại";

                    return new TopProductReportDto
                    {
                        ProductId = g.Key,
                        ProductName = prod?.Name ?? $"Sản phẩm #{g.Key}",
                        CategoryName = catName,
                        Unit = prod?.Unit ?? "kg",
                        Image = prod?.Image,
                        TotalQuantitySold = g.Sum(x => x.Quantity),
                        TotalRevenue = g.Sum(x => x.Quantity * x.Price),
                        OrderCount = g.Select(x => x.OrderId).Distinct().Count(),
                        CurrentStock = prod?.Quantity ?? 0
                    };
                })
                .OrderByDescending(x => x.TotalQuantitySold)
                .ThenByDescending(x => x.TotalRevenue)
                .Take(safeTop)
                .ToList();

            return Ok(result);
        }

        // 3. GET: api/Analytics/category-share?days=30
        [HttpGet("category-share")]
        public async Task<IActionResult> GetCategoryShare(
            [FromQuery] int days = 30,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var (startDay, endExclusive) = ResolveDateRange(days, fromDate, toDate);

            var validOrderIds = await _context.Orders
                .AsNoTracking()
                .Where(o => o.OrderDate >= startDay &&
                            o.OrderDate < endExclusive &&
                            o.Status != "Cancelled")
                .Select(o => o.Id)
                .ToListAsync();

            var categories = await _context.Categories.AsNoTracking().ToListAsync();
            if (validOrderIds.Count == 0)
            {
                return Ok(categories.Select(c => new CategoryRevenueDto
                {
                    CategoryId = c.Id,
                    CategoryName = c.Name,
                    TotalQuantitySold = 0,
                    TotalRevenue = 0,
                    Percentage = 0
                }));
            }

            var lines = await _context.OrderDetails
                .AsNoTracking()
                .Where(d => validOrderIds.Contains(d.OrderId))
                .Select(d => new { d.ProductId, d.Quantity, d.Price })
                .ToListAsync();

            var productCatMap = await _context.Products
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => p.CategoryId);

            var totalRevenueAll = lines.Sum(l => l.Quantity * l.Price);

            var grouped = lines
                .GroupBy(l => productCatMap.TryGetValue(l.ProductId, out var catId) ? catId : 0)
                .ToDictionary(g => g.Key, g => new
                {
                    Qty = g.Sum(x => x.Quantity),
                    Rev = g.Sum(x => x.Quantity * x.Price)
                });

            var result = categories
                .Select(c =>
                {
                    grouped.TryGetValue(c.Id, out var stat);
                    var rev = stat?.Rev ?? 0;
                    return new CategoryRevenueDto
                    {
                        CategoryId = c.Id,
                        CategoryName = c.Name,
                        TotalQuantitySold = stat?.Qty ?? 0,
                        TotalRevenue = rev,
                        Percentage = totalRevenueAll > 0
                            ? Math.Round((double)(rev / totalRevenueAll) * 100, 1)
                            : 0
                    };
                })
                .OrderByDescending(x => x.TotalRevenue)
                .ToList();

            return Ok(result);
        }

        // 4. GET: api/Analytics/rates?days=30 (Tỷ lệ hủy đơn & Tỷ lệ hao hụt/hư hỏng rau củ)
        [HttpGet("rates")]
        public async Task<IActionResult> GetRatesAndSpoilage(
            [FromQuery] int days = 30,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            if (!await _permissions.CanManageOrdersAsync(User))
                return Forbid();

            var (startDay, endExclusive) = ResolveDateRange(days, fromDate, toDate);

            // A. Thống kê đơn hàng
            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.OrderDate >= startDay && o.OrderDate < endExclusive)
                .Select(o => new { o.Id, o.Status })
                .ToListAsync();

            var totalOrders = orders.Count;
            var completedOrders = orders.Count(o => string.Equals(o.Status, "Completed", StringComparison.OrdinalIgnoreCase));
            var pendingOrders = orders.Count(o => string.Equals(o.Status, "Pending", StringComparison.OrdinalIgnoreCase));
            var processingOrders = orders.Count(o => string.Equals(o.Status, "Processing", StringComparison.OrdinalIgnoreCase));
            var shippingOrders = orders.Count(o => string.Equals(o.Status, "Shipping", StringComparison.OrdinalIgnoreCase));
            var cancelledOrders = orders.Count(o => string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

            var validOrderIds = orders
                .Where(o => !string.Equals(o.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .Select(o => o.Id)
                .ToList();

            var totalSoldUnits = validOrderIds.Count > 0
                ? await _context.OrderDetails
                    .AsNoTracking()
                    .Where(d => validOrderIds.Contains(d.OrderId))
                    .SumAsync(d => d.Quantity)
                : 0;

            // B. Thống kê nhập lô & xuất hủy rau củ
            var totalImportedUnits = await _context.ProductBatches
                .AsNoTracking()
                .Where(b => b.ImportDate >= startDay && b.ImportDate < endExclusive)
                .SumAsync(b => b.InitialQuantity);

            var disposals = await _context.BatchDisposalLogs
                .AsNoTracking()
                .Include(d => d.ProductBatch)
                    .ThenInclude(b => b!.Product)
                .Where(d => d.DisposedAt >= startDay && d.DisposedAt < endExclusive)
                .ToListAsync();

            var totalDisposedUnits = disposals.Sum(d => d.QuantityDisposed);
            var estimatedLossValue = disposals.Sum(d =>
                d.QuantityDisposed * (d.ProductBatch?.ImportPrice ?? d.ProductBatch?.Product?.Price ?? 0));

            var totalHandledUnits = totalSoldUnits + totalDisposedUnits;
            var spoilageRate = totalHandledUnits > 0
                ? Math.Round((double)totalDisposedUnits / totalHandledUnits * 100, 1)
                : 0;

            var topSpoiledProducts = disposals
                .Where(d => d.ProductBatch != null)
                .GroupBy(d => d.ProductBatch!.ProductId)
                .Select(g =>
                {
                    var first = g.First();
                    var prod = first.ProductBatch?.Product;
                    return new SpoiledProductItemDto
                    {
                        ProductId = g.Key,
                        ProductName = prod?.Name ?? $"Sản phẩm #{g.Key}",
                        Unit = prod?.Unit ?? "kg",
                        DisposedQuantity = g.Sum(x => x.QuantityDisposed),
                        EstimatedLoss = g.Sum(x => x.QuantityDisposed * (x.ProductBatch?.ImportPrice ?? prod?.Price ?? 0)),
                        LastReason = g.OrderByDescending(x => x.DisposedAt).First().Reason
                    };
                })
                .OrderByDescending(x => x.DisposedQuantity)
                .Take(5)
                .ToList();

            var reasonBreakdown = disposals
                .GroupBy(d => string.IsNullOrWhiteSpace(d.Reason) ? "Khác" : d.Reason.Trim())
                .Select(g => new DisposalReasonItemDto
                {
                    Reason = g.Key,
                    Count = g.Count(),
                    TotalQuantity = g.Sum(x => x.QuantityDisposed)
                })
                .OrderByDescending(x => x.TotalQuantity)
                .ToList();

            return Ok(new AnalyticsRatesResponseDto
            {
                FromDate = startDay.ToString("yyyy-MM-dd"),
                ToDate = endExclusive.AddDays(-1).ToString("yyyy-MM-dd"),
                TotalOrders = totalOrders,
                CompletedOrders = completedOrders,
                PendingOrders = pendingOrders,
                ProcessingOrders = processingOrders,
                ShippingOrders = shippingOrders,
                CancelledOrders = cancelledOrders,
                CancellationRate = totalOrders > 0 ? Math.Round((double)cancelledOrders / totalOrders * 100, 1) : 0,
                CompletionRate = totalOrders > 0 ? Math.Round((double)completedOrders / totalOrders * 100, 1) : 0,
                TotalSoldUnits = totalSoldUnits,
                TotalImportedUnits = totalImportedUnits,
                TotalDisposedUnits = totalDisposedUnits,
                SpoilageRate = spoilageRate,
                EstimatedLossValue = estimatedLossValue,
                TopSpoiledProducts = topSpoiledProducts,
                ReasonBreakdown = reasonBreakdown
            });
        }

        private static (DateTime StartDay, DateTime EndExclusive) ResolveDateRange(
            int days,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var safeDays = Math.Clamp(days, 1, 365);
            var endDay = (toDate ?? DateTime.Today).Date;
            var startDay = (fromDate ?? endDay.AddDays(-(safeDays - 1))).Date;
            if (startDay > endDay)
                (startDay, endDay) = (endDay, startDay);

            return (startDay, endDay.AddDays(1));
        }
    }
}
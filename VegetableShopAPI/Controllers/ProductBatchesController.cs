using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProductBatchesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public ProductBatchesController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        // GET: api/ProductBatches/product/5 (Lấy danh sách lô hàng của 1 sản phẩm, kèm nhà cung cấp & chứng nhận)
        [HttpGet("product/{productId:int}")]
        public async Task<IActionResult> GetBatchesByProduct(int productId)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var product = await _context.Products
                .AsNoTracking()
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });

            await RecalculateProductStockAsync(_context, productId);
            await _context.SaveChangesAsync();

            var variantMap = (product.Variants ?? new List<ProductVariant>())
                .ToDictionary(v => v.Id, v => v.Name);

            var batches = await _context.ProductBatches
                .AsNoTracking()
                .Include(b => b.Supplier)
                .Where(b => b.ProductId == productId)
                .OrderBy(b => b.RemainingQuantity <= 0 ? 1 : 0)
                .ThenBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .ToListAsync();

            var result = batches.Select(b => new
            {
                b.Id,
                b.BatchCode,
                b.ProductId,
                b.ProductVariantId,
                VariantName = b.ProductVariantId.HasValue && variantMap.TryGetValue(b.ProductVariantId.Value, out var vName)
                    ? vName
                    : null,
                b.SupplierId,
                SupplierName = b.Supplier != null ? b.Supplier.Name : b.SupplierName,
                CertificationType = b.Supplier?.CertificationType,
                CertificationCode = b.Supplier?.CertificationCode,
                SupplierAddress = b.Supplier?.Address,
                b.ImportDate,
                b.ExpiryDate,
                b.InitialQuantity,
                b.RemainingQuantity,
                b.ImportPrice,
                b.Note,
                b.DaysUntilExpiry,
                b.Status
            }).ToList();

            var validStock = result.Where(x => x.Status == "Valid" || x.Status == "NearExpiry").Sum(x => x.RemainingQuantity);
            var nearExpiryStock = result.Where(x => x.Status == "NearExpiry").Sum(x => x.RemainingQuantity);
            var expiredStock = result.Where(x => x.Status == "Expired").Sum(x => x.RemainingQuantity);

            return Ok(new
            {
                productId = product.Id,
                productName = product.Name,
                unit = product.Unit,
                summary = new
                {
                    validStock,
                    nearExpiryStock,
                    expiredStock,
                    nearExpiryBatchCount = result.Count(x => x.Status == "NearExpiry"),
                    expiredBatchCount = result.Count(x => x.Status == "Expired")
                },
                batches = result
            });
        }

        // GET: api/ProductBatches/alerts?days=3 (Lấy cảnh báo cận date & hết hạn cho toàn bộ kho)
        [HttpGet("alerts")]
        [AllowAnonymous]
        public async Task<IActionResult> GetBatchAlerts([FromQuery] int days = 3)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            if (days < 1) days = 3;
            var today = DateTime.Today;
            var thresholdDate = today.AddDays(days);

            var activeBatches = await (
                from b in _context.ProductBatches.AsNoTracking()
                where b.RemainingQuantity > 0 && b.ExpiryDate.Date <= thresholdDate
                join p in _context.Products.AsNoTracking() on b.ProductId equals p.Id
                join v in _context.ProductVariants.AsNoTracking() on b.ProductVariantId equals (int?)v.Id into vGroup
                from variant in vGroup.DefaultIfEmpty()
                join s in _context.Suppliers.AsNoTracking() on b.SupplierId equals (int?)s.Id into sGroup
                from supplier in sGroup.DefaultIfEmpty()
                orderby b.ExpiryDate ascending, b.Id ascending
                select new
                {
                    b.Id,
                    b.BatchCode,
                    b.ProductId,
                    ProductName = p.Name,
                    Unit = variant != null && variant.Unit != "" ? variant.Unit : p.Unit,
                    b.ProductVariantId,
                    VariantName = variant != null ? variant.Name : null,
                    b.SupplierId,
                    SupplierName = supplier != null ? supplier.Name : b.SupplierName,
                    CertificationType = supplier != null ? supplier.CertificationType : null,
                    b.ImportDate,
                    b.ExpiryDate,
                    b.RemainingQuantity,
                    DaysUntilExpiry = (b.ExpiryDate.Date - today).Days,
                    Status = b.ExpiryDate.Date < today ? "Expired" : "NearExpiry"
                }).ToListAsync();

            var byProduct = activeBatches
                .GroupBy(x => x.ProductId)
                .Select(g => new
                {
                    productId = g.Key,
                    nearExpiryQuantity = g.Where(x => x.Status == "NearExpiry").Sum(x => x.RemainingQuantity),
                    expiredQuantity = g.Where(x => x.Status == "Expired").Sum(x => x.RemainingQuantity),
                    nearestExpiryDate = g.Min(x => x.ExpiryDate),
                    minDaysUntilExpiry = g.Min(x => x.DaysUntilExpiry),
                    hasExpired = g.Any(x => x.Status == "Expired"),
                    hasNearExpiry = g.Any(x => x.Status == "NearExpiry")
                })
                .ToList();

            return Ok(new
            {
                nearExpiryTotalBatches = activeBatches.Count(x => x.Status == "NearExpiry"),
                expiredTotalBatches = activeBatches.Count(x => x.Status == "Expired"),
                byProduct,
                batches = activeBatches
            });
        }

        // POST: api/ProductBatches/import (Nhập lô hàng mới gắn với Nhà cung cấp / Nhà vườn)
        [HttpPost("import")]
        public async Task<IActionResult> ImportBatch([FromBody] ImportBatchWithSupplierDto dto)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == dto.ProductId);

            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });

            var hasVariants = product.Variants != null && product.Variants.Count > 0;
            if (hasVariants)
            {
                if (!dto.ProductVariantId.HasValue)
                    return BadRequest(new { message = "Sản phẩm này có nhiều loại, vui lòng chọn loại cụ thể khi nhập lô." });

                var variantExists = product.Variants!.Any(v => v.Id == dto.ProductVariantId.Value);
                if (!variantExists)
                    return BadRequest(new { message = "Loại sản phẩm không hợp lệ." });
            }
            else
            {
                dto.ProductVariantId = null;
            }

            Supplier? supplier = null;
            if (dto.SupplierId.HasValue)
            {
                supplier = await _context.Suppliers
                    .FirstOrDefaultAsync(s => s.Id == dto.SupplierId.Value);

                if (supplier == null)
                    return BadRequest(new { message = "Nhà cung cấp / nhà vườn được chọn không tồn tại." });

                if (!supplier.IsActive)
                    return BadRequest(new { message = $"Nhà cung cấp “{supplier.Name}” hiện đang ngừng hợp tác." });
            }

            var importDate = dto.ImportDate ?? DateTime.Now;
            if (dto.ExpiryDate.Date < importDate.Date)
                return BadRequest(new { message = "Hạn sử dụng không được nhỏ hơn ngày nhập kho." });

            var batchCode = string.IsNullOrWhiteSpace(dto.BatchCode)
                ? await GenerateBatchCodeAsync(dto.ProductId)
                : dto.BatchCode.Trim().ToUpperInvariant();

            var batch = new ProductBatch
            {
                BatchCode = batchCode,
                ProductId = dto.ProductId,
                ProductVariantId = dto.ProductVariantId,
                SupplierId = supplier?.Id,
                ImportDate = importDate,
                ExpiryDate = dto.ExpiryDate.Date.AddHours(23).AddMinutes(59),
                InitialQuantity = dto.Quantity,
                RemainingQuantity = dto.Quantity,
                ImportPrice = dto.ImportPrice,
                SupplierName = supplier != null
                    ? supplier.Name
                    : (string.IsNullOrWhiteSpace(dto.SupplierName) ? null : dto.SupplierName.Trim()),
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()
            };

            _context.ProductBatches.Add(batch);
            await _context.SaveChangesAsync();

            await RecalculateProductStockAsync(_context, dto.ProductId);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã nhập lô {batch.BatchCode} (+{batch.InitialQuantity} {product.Unit}).",
                batch,
                productQuantity = product.Quantity
            });
        }

        // POST: api/ProductBatches/dispose (Xuất hủy hàng hỏng / hết hạn)
        [HttpPost("dispose")]
        public async Task<IActionResult> DisposeBatch([FromBody] DisposeBatchDto dto)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var batch = await _context.ProductBatches.FirstOrDefaultAsync(b => b.Id == dto.BatchId);
            if (batch == null)
                return NotFound(new { message = "Không tìm thấy lô hàng!" });

            if (dto.Quantity > batch.RemainingQuantity)
                return BadRequest(new { message = $"Số lượng hủy ({dto.Quantity}) vượt quá tồn lô hiện tại ({batch.RemainingQuantity})." });

            int? userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            batch.RemainingQuantity -= dto.Quantity;

            var log = new BatchDisposalLog
            {
                ProductBatchId = batch.Id,
                ProductId = batch.ProductId,
                ProductVariantId = batch.ProductVariantId,
                QuantityDisposed = dto.Quantity,
                Reason = dto.Reason.Trim(),
                DisposedAt = DateTime.Now,
                DisposedByUserId = userId
            };

            _context.BatchDisposalLogs.Add(log);
            await _context.SaveChangesAsync();

            await RecalculateProductStockAsync(_context, batch.ProductId);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã xuất hủy {dto.Quantity} sản phẩm từ lô {batch.BatchCode}.",
                remainingInBatch = batch.RemainingQuantity
            });
        }

        // GET: api/ProductBatches/disposals?productId=5 (Lịch sử xuất hủy)
        [HttpGet("disposals")]
        public async Task<IActionResult> GetDisposalLogs([FromQuery] int? productId)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var query = _context.BatchDisposalLogs.AsNoTracking().AsQueryable();
            if (productId.HasValue)
                query = query.Where(d => d.ProductId == productId.Value);

            var logs = await (
                from d in query
                join b in _context.ProductBatches.AsNoTracking() on d.ProductBatchId equals b.Id
                join p in _context.Products.AsNoTracking() on d.ProductId equals p.Id
                join u in _context.Users.AsNoTracking() on d.DisposedByUserId equals (int?)u.Id into uGroup
                from user in uGroup.DefaultIfEmpty()
                orderby d.DisposedAt descending, d.Id descending
                select new
                {
                    d.Id,
                    b.BatchCode,
                    d.ProductId,
                    ProductName = p.Name,
                    Unit = p.Unit,
                    d.QuantityDisposed,
                    d.Reason,
                    d.DisposedAt,
                    DisposedBy = user != null ? user.FullName : "Quản trị viên"
                }).Take(100).ToListAsync();

            return Ok(logs);
        }

        // ===================== HÀM DÙNG CHUNG ĐỒNG BỘ TỒN KHO =====================

        private async Task<string> GenerateBatchCodeAsync(int productId)
        {
            var datePrefix = DateTime.Now.ToString("yyyyMMdd");
            var countToday = await _context.ProductBatches
                .CountAsync(b => b.ProductId == productId && b.ImportDate.Date == DateTime.Today);

            return $"LO-SP{productId}-{datePrefix}-{(countToday + 1):D2}";
        }

        public static async Task RecalculateProductStockAsync(ApplicationDbContext context, int productId)
        {
            var hasAnyBatches = await context.ProductBatches.AnyAsync(b => b.ProductId == productId);
            if (!hasAnyBatches)
                return;

            var product = await context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
                return;

            var today = DateTime.Today;
            var batches = await context.ProductBatches
                .Where(b => b.ProductId == productId)
                .ToListAsync();

            if (product.Variants != null && product.Variants.Count > 0)
            {
                foreach (var variant in product.Variants)
                {
                    var variantHasBatches = batches.Any(b => b.ProductVariantId == variant.Id);
                    if (variantHasBatches)
                    {
                        variant.Quantity = batches
                            .Where(b => b.ProductVariantId == variant.Id && b.ExpiryDate.Date >= today)
                            .Sum(b => b.RemainingQuantity);
                    }
                }

                product.Quantity = product.Variants
                    .Where(v => v.IsVisible)
                    .Sum(v => v.Quantity);
            }
            else
            {
                product.Quantity = batches
                    .Where(b => b.ProductVariantId == null && b.ExpiryDate.Date >= today)
                    .Sum(b => b.RemainingQuantity);
            }
        }
    }

    public class ImportBatchWithSupplierDto : CreateBatchDto
    {
        public int? SupplierId { get; set; }
    }
}
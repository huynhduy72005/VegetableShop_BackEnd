using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductsController : ControllerBase
    {
        private const int MaxVariantsPerProduct = 20;

        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public ProductsController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        // GET: api/products (Lấy danh sách tất cả sản phẩm, tự động loại trừ tồn kho đã hết hạn)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            await SyncExpiredBatchesIfNeededAsync();

            return await _context.Products
                .AsNoTracking()
                .Include(p => p.Variants!.OrderBy(v => v.Id))
                .ToListAsync();
        }

        // GET: api/products/5 (Lấy chi tiết sản phẩm theo ID, kèm biến thể & truy xuất nguồn gốc lô hiện tại)
        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            await SyncExpiredBatchesIfNeededAsync(id);

            var product = await _context.Products
                .AsNoTracking()
                .Include(p => p.Variants!.OrderBy(v => v.Id))
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });
            }

            var today = DateTime.Today;
            var activeBatch = await _context.ProductBatches
                .AsNoTracking()
                .Include(b => b.Supplier)
                .Where(b => b.ProductId == id && b.RemainingQuantity > 0 && b.ExpiryDate >= today)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .FirstOrDefaultAsync();

            object? origin = null;
            if (activeBatch != null && (activeBatch.Supplier != null || !string.IsNullOrWhiteSpace(activeBatch.SupplierName)))
            {
                origin = new
                {
                    batchCode = activeBatch.BatchCode,
                    importDate = activeBatch.ImportDate,
                    expiryDate = activeBatch.ExpiryDate,
                    supplierId = activeBatch.SupplierId,
                    supplierName = activeBatch.Supplier?.Name ?? activeBatch.SupplierName,
                    farmAddress = activeBatch.Supplier?.Address,
                    certificationType = activeBatch.Supplier?.CertificationType ?? "Thông thường",
                    certificationCode = activeBatch.Supplier?.CertificationCode,
                    certificationExpiryDate = activeBatch.Supplier?.CertificationExpiryDate
                };
            }

            return Ok(new
            {
                product.Id,
                product.Name,
                product.Price,
                product.Quantity,
                product.Unit,
                product.Image,
                product.Description,
                product.CategoryId,
                product.IsVisible,
                product.Variants,
                Origin = origin
            });
        }

        // POST: api/products (Thêm sản phẩm mới, có thể kèm 1 hoặc nhiều biến thể)
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Product>> PostProduct(Product product)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            var variantError = NormalizeVariants(product.Variants, product.Unit);
            if (variantError != null)
                return BadRequest(new { message = variantError });

            foreach (var variant in product.Variants ?? new List<ProductVariant>())
                variant.Id = 0;

            ApplyVariantTotals(product, product.Variants);

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PUT: api/products/5 (Cập nhật sản phẩm + THÊM / SỬA / XÓA biến thể)
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> UpdateProduct(int id, Product product)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            if (id != product.Id)
            {
                return BadRequest(new { message = "ID sản phẩm không khớp!" });
            }

            var existing = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (existing == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm cần sửa!" });
            }

            var incoming = product.Variants;
            if (incoming != null)
            {
                var variantError = NormalizeVariants(incoming, product.Unit);
                if (variantError != null)
                    return BadRequest(new { message = variantError });
            }

            _context.Entry(existing).CurrentValues.SetValues(product);

            var currentVariants = existing.Variants ??= new List<ProductVariant>();

            if (incoming != null)
            {
                var keepIds = incoming.Where(v => v.Id > 0).Select(v => v.Id).ToHashSet();

                // 1) XÓA biến thể không còn trong danh sách gửi lên
                foreach (var removed in currentVariants.Where(v => !keepIds.Contains(v.Id)).ToList())
                {
                    var variantBatches = await _context.ProductBatches
                        .Where(b => b.ProductVariantId == removed.Id)
                        .ToListAsync();
                    if (variantBatches.Count > 0)
                        _context.ProductBatches.RemoveRange(variantBatches);

                    currentVariants.Remove(removed);
                    _context.ProductVariants.Remove(removed);
                }

                // 2) SỬA biến thể cũ / 3) THÊM biến thể mới
                foreach (var item in incoming)
                {
                    var target = item.Id > 0
                        ? currentVariants.FirstOrDefault(v => v.Id == item.Id)
                        : null;

                    if (target != null)
                    {
                        target.Name = item.Name;
                        target.Unit = item.Unit;
                        target.Price = item.Price;
                        target.Quantity = item.Quantity;
                        target.Image = item.Image;
                        target.IsDefault = item.IsDefault;
                        target.IsVisible = item.IsVisible;
                    }
                    else
                    {
                        currentVariants.Add(new ProductVariant
                        {
                            Name = item.Name,
                            Unit = item.Unit,
                            Price = item.Price,
                            Quantity = item.Quantity,
                            Image = item.Image,
                            IsDefault = item.IsDefault,
                            IsVisible = item.IsVisible
                        });
                    }
                }
            }

            ApplyVariantTotals(existing, currentVariants);

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Cập nhật sản phẩm thành công!" });
        }

        // PUT: api/products/5/inventory
        [HttpPut("{id}/inventory")]
        [Authorize]
        public async Task<IActionResult> UpdateInventory(
            int id,
            [FromBody] InventoryUpdateDto request)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });

            if (product.Variants != null && product.Variants.Count > 0)
            {
                if (request.Variants == null || request.Variants.Count == 0)
                    return BadRequest(new { message = "Sản phẩm này có nhiều loại, vui lòng cập nhật tồn kho theo từng loại." });

                foreach (var item in request.Variants)
                {
                    var variant = product.Variants.FirstOrDefault(v => v.Id == item.Id);
                    if (variant == null)
                        return BadRequest(new { message = "Có loại không thuộc sản phẩm này." });

                    variant.Quantity = item.Quantity;
                }

                ApplyVariantTotals(product, product.Variants);
            }
            else
            {
                if (request.Quantity == null)
                    return BadRequest(new { message = "Thiếu số lượng tồn kho." });

                product.Quantity = request.Quantity.Value;
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, product.Id, product.Quantity });
        }

        // DELETE: api/products/5 (Xóa sản phẩm)
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });
            }

            bool hasOrders = await _context.OrderDetails.AnyAsync(od => od.ProductId == id);
            if (hasOrders)
            {
                return BadRequest(new
                {
                    message = "Không thể xóa vì sản phẩm này đã phát sinh đơn hàng. Hãy bấm nút Ẩn (biểu tượng con mắt) để ngừng bán!"
                });
            }

            try
            {
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Xóa sản phẩm thành công!" });
            }
            catch (DbUpdateException)
            {
                return BadRequest(new
                {
                    message = "Không thể xóa sản phẩm do đang được tham chiếu ở dữ liệu khác."
                });
            }
        }

        // ===================== HÀM HỖ TRỢ LÔ HÀNG & BIẾN THỂ =====================

        private async Task SyncExpiredBatchesIfNeededAsync(int? onlyProductId = null)
        {
            var today = DateTime.Today;
            var query = _context.ProductBatches
                .AsNoTracking()
                .Where(b => b.RemainingQuantity > 0 && b.ExpiryDate < today);

            if (onlyProductId.HasValue)
                query = query.Where(b => b.ProductId == onlyProductId.Value);

            var affectedProductIds = await query
                .Select(b => b.ProductId)
                .Distinct()
                .ToListAsync();

            if (affectedProductIds.Count == 0)
                return;

            foreach (var pid in affectedProductIds)
            {
                await ProductBatchesController.RecalculateProductStockAsync(_context, pid);
            }

            await _context.SaveChangesAsync();
        }

        private static string? NormalizeVariants(List<ProductVariant>? variants, string productUnit)
        {
            if (variants == null || variants.Count == 0)
                return null;

            if (variants.Count > MaxVariantsPerProduct)
                return $"Mỗi sản phẩm tối đa {MaxVariantsPerProduct} biến thể.";

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                variant.Name = (variant.Name ?? string.Empty).Trim();
                variant.Unit = string.IsNullOrWhiteSpace(variant.Unit)
                    ? (productUnit ?? string.Empty).Trim()
                    : variant.Unit.Trim();
                variant.Image = string.IsNullOrWhiteSpace(variant.Image) ? null : variant.Image.Trim();
                variant.ProductId = 0;

                if (variant.Name.Length == 0)
                    return $"Biến thể số {i + 1} chưa có tên.";
                if (variant.Name.Length > 100)
                    return $"Tên biến thể “{variant.Name[..20]}…” quá dài (tối đa 100 ký tự).";
                if (variant.Unit.Length > 30)
                    return $"Đơn vị của biến thể “{variant.Name}” quá dài (tối đa 30 ký tự).";
                if (variant.Price < 0)
                    return $"Giá của biến thể “{variant.Name}” không hợp lệ.";
                if (variant.Quantity < 0)
                    return $"Tồn kho của biến thể “{variant.Name}” không hợp lệ.";
                if (!names.Add(variant.Name))
                    return $"Tên biến thể “{variant.Name}” bị trùng.";
            }

            var defaultIndex = variants.FindIndex(v => v.IsDefault && v.IsVisible);
            if (defaultIndex < 0) defaultIndex = variants.FindIndex(v => v.IsVisible);
            if (defaultIndex < 0) defaultIndex = 0;
            for (var i = 0; i < variants.Count; i++)
                variants[i].IsDefault = i == defaultIndex;

            return null;
        }

        private static void ApplyVariantTotals(Product product, List<ProductVariant>? variants)
        {
            if (variants == null || variants.Count == 0)
                return;

            var selling = variants.Where(v => v.IsVisible).ToList();
            product.Price = (selling.Count > 0 ? selling : variants).Min(v => v.Price);
            product.Quantity = selling.Sum(v => v.Quantity);
        }

        public sealed class InventoryUpdateDto
        {
            [Range(0, int.MaxValue)]
            public int? Quantity { get; set; }

            public List<VariantInventoryDto>? Variants { get; set; }
        }

        public sealed class VariantInventoryDto
        {
            public int Id { get; set; }

            [Range(0, int.MaxValue)]
            public int Quantity { get; set; }
        }
    }
}
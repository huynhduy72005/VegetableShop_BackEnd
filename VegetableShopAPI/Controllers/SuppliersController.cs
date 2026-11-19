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
    public class SuppliersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        private static readonly string[] AllowedCertifications =
        {
            "VietGAP",
            "GlobalGAP",
            "Hữu cơ (Organic)",
            "VietGAP & GlobalGAP",
            "Thông thường"
        };

        public SuppliersController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        private async Task<bool> CanAccessSuppliersAsync()
        {
            return await _permissions.CanManageInventoryAsync(User) ||
                   await _permissions.CanManageProductsAsync(User);
        }

        // GET: api/Suppliers?search=&certification=&isActive=true
        [HttpGet]
        public async Task<IActionResult> GetSuppliers(
            [FromQuery] string? search = null,
            [FromQuery] string? certification = null,
            [FromQuery] bool? isActive = null)
        {
            if (!await CanAccessSuppliersAsync())
                return Forbid();

            var query = _context.Suppliers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLower();
                query = query.Where(s =>
                    s.Name.ToLower().Contains(q) ||
                    (s.ContactPerson != null && s.ContactPerson.ToLower().Contains(q)) ||
                    (s.Phone != null && s.Phone.ToLower().Contains(q)) ||
                    (s.Address != null && s.Address.ToLower().Contains(q)) ||
                    (s.CertificationCode != null && s.CertificationCode.ToLower().Contains(q)));
            }

            if (!string.IsNullOrWhiteSpace(certification))
            {
                var cert = certification.Trim();
                query = query.Where(s => s.CertificationType == cert);
            }

            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            var suppliers = await query
                .OrderByDescending(s => s.IsActive)
                .ThenByDescending(s => s.Id)
                .ToListAsync();

            var supplierIds = suppliers.Select(s => s.Id).ToList();
            var today = DateTime.Today;

            var batchStats = supplierIds.Count > 0
                ? await _context.ProductBatches
                    .AsNoTracking()
                    .Where(b => b.SupplierId.HasValue && supplierIds.Contains(b.SupplierId.Value))
                    .GroupBy(b => b.SupplierId!.Value)
                    .Select(g => new
                    {
                        SupplierId = g.Key,
                        TotalBatches = g.Count(),
                        ActiveBatches = g.Count(b => b.RemainingQuantity > 0 && b.ExpiryDate >= today),
                        TotalSuppliedQuantity = g.Sum(b => b.InitialQuantity),
                        LastImportDate = g.Max(b => (DateTime?)b.ImportDate)
                    })
                    .ToDictionaryAsync(x => x.SupplierId)
                : new();

            var result = suppliers.Select(s =>
            {
                batchStats.TryGetValue(s.Id, out var stat);
                return new SupplierSummaryDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    ContactPerson = s.ContactPerson,
                    Phone = s.Phone,
                    Email = s.Email,
                    Address = s.Address,
                    CertificationType = s.CertificationType,
                    CertificationCode = s.CertificationCode,
                    CertificationExpiryDate = s.CertificationExpiryDate,
                    IsCertificationExpired = s.IsCertificationExpired,
                    IsCertificationNearExpiry = s.IsCertificationNearExpiry,
                    Notes = s.Notes,
                    IsActive = s.IsActive,
                    CreatedAt = s.CreatedAt,
                    TotalBatches = stat?.TotalBatches ?? 0,
                    ActiveBatches = stat?.ActiveBatches ?? 0,
                    TotalSuppliedQuantity = stat?.TotalSuppliedQuantity ?? 0,
                    LastImportDate = stat?.LastImportDate
                };
            }).ToList();

            return Ok(result);
        }

        // GET: api/Suppliers/5 (Chi tiết nhà vườn & lịch sử các lô nông sản đã nhập)
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetSupplierById(int id)
        {
            if (!await CanAccessSuppliersAsync())
                return Forbid();

            var supplier = await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (supplier == null)
                return NotFound(new { message = "Không tìm thấy nhà cung cấp / nhà vườn!" });

            var today = DateTime.Today;
            var rawBatches = await (
                from b in _context.ProductBatches.AsNoTracking()
                where b.SupplierId == id
                join p in _context.Products.AsNoTracking() on b.ProductId equals p.Id
                join v in _context.ProductVariants.AsNoTracking() on b.ProductVariantId equals (int?)v.Id into vGroup
                from variant in vGroup.DefaultIfEmpty()
                orderby b.ImportDate descending, b.Id descending
                select new
                {
                    b.Id,
                    b.BatchCode,
                    b.ProductId,
                    ProductName = p.Name,
                    VariantName = variant != null ? variant.Name : null,
                    Unit = variant != null && variant.Unit != "" ? variant.Unit : p.Unit,
                    b.ImportDate,
                    b.ExpiryDate,
                    b.InitialQuantity,
                    b.RemainingQuantity,
                    b.ImportPrice,
                    b.Note
                }).ToListAsync();

            var batches = rawBatches.Select(b =>
            {
                var daysLeft = (b.ExpiryDate.Date - today).Days;
                var status = b.RemainingQuantity <= 0
                    ? "Depleted"
                    : daysLeft < 0
                        ? "Expired"
                        : daysLeft <= 3
                            ? "NearExpiry"
                            : "Valid";

                return new SupplierBatchHistoryDto
                {
                    BatchId = b.Id,
                    BatchCode = b.BatchCode,
                    ProductId = b.ProductId,
                    ProductName = b.ProductName,
                    VariantName = b.VariantName,
                    Unit = b.Unit,
                    ImportDate = b.ImportDate,
                    ExpiryDate = b.ExpiryDate,
                    InitialQuantity = b.InitialQuantity,
                    RemainingQuantity = b.RemainingQuantity,
                    ImportPrice = b.ImportPrice,
                    Status = status,
                    Note = b.Note
                };
            }).ToList();

            var detail = new SupplierDetailDto
            {
                Id = supplier.Id,
                Name = supplier.Name,
                ContactPerson = supplier.ContactPerson,
                Phone = supplier.Phone,
                Email = supplier.Email,
                Address = supplier.Address,
                CertificationType = supplier.CertificationType,
                CertificationCode = supplier.CertificationCode,
                CertificationExpiryDate = supplier.CertificationExpiryDate,
                IsCertificationExpired = supplier.IsCertificationExpired,
                IsCertificationNearExpiry = supplier.IsCertificationNearExpiry,
                Notes = supplier.Notes,
                IsActive = supplier.IsActive,
                CreatedAt = supplier.CreatedAt,
                TotalBatches = batches.Count,
                ActiveBatches = batches.Count(x => x.RemainingQuantity > 0 && x.ExpiryDate.Date >= today),
                TotalSuppliedQuantity = batches.Sum(x => x.InitialQuantity),
                TotalImportValue = batches.Sum(x => x.InitialQuantity * (x.ImportPrice ?? 0)),
                LastImportDate = batches.FirstOrDefault()?.ImportDate,
                Batches = batches
            };

            return Ok(detail);
        }

        // POST: api/Suppliers
        [HttpPost]
        public async Task<IActionResult> CreateSupplier([FromBody] UpsertSupplierDto dto)
        {
            if (!await CanAccessSuppliersAsync())
                return Forbid();

            var name = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Vui lòng nhập tên nhà cung cấp / nhà vườn." });

            var exists = await _context.Suppliers
                .AnyAsync(s => s.Name.ToLower() == name.ToLower());
            if (exists)
                return Conflict(new { message = $"Nhà cung cấp “{name}” đã tồn tại trong hệ thống." });

            var supplier = new Supplier
            {
                Name = name,
                ContactPerson = NormalizeOptional(dto.ContactPerson),
                Phone = NormalizeOptional(dto.Phone),
                Email = NormalizeOptional(dto.Email),
                Address = NormalizeOptional(dto.Address),
                CertificationType = NormalizeCertification(dto.CertificationType),
                CertificationCode = NormalizeOptional(dto.CertificationCode)?.ToUpperInvariant(),
                CertificationExpiryDate = dto.CertificationExpiryDate?.Date,
                Notes = NormalizeOptional(dto.Notes),
                IsActive = dto.IsActive,
                CreatedAt = DateTime.Now
            };

            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã thêm nhà cung cấp “{supplier.Name}”.",
                supplier
            });
        }

        // PUT: api/Suppliers/5
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateSupplier(int id, [FromBody] UpsertSupplierDto dto)
        {
            if (!await CanAccessSuppliersAsync())
                return Forbid();

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
            if (supplier == null)
                return NotFound(new { message = "Không tìm thấy nhà cung cấp / nhà vườn!" });

            var name = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Vui lòng nhập tên nhà cung cấp / nhà vườn." });

            var duplicate = await _context.Suppliers
                .AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower());
            if (duplicate)
                return Conflict(new { message = $"Tên nhà cung cấp “{name}” đã được sử dụng." });

            supplier.Name = name;
            supplier.ContactPerson = NormalizeOptional(dto.ContactPerson);
            supplier.Phone = NormalizeOptional(dto.Phone);
            supplier.Email = NormalizeOptional(dto.Email);
            supplier.Address = NormalizeOptional(dto.Address);
            supplier.CertificationType = NormalizeCertification(dto.CertificationType);
            supplier.CertificationCode = NormalizeOptional(dto.CertificationCode)?.ToUpperInvariant();
            supplier.CertificationExpiryDate = dto.CertificationExpiryDate?.Date;
            supplier.Notes = NormalizeOptional(dto.Notes);
            supplier.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã cập nhật thông tin “{supplier.Name}”.",
                supplier
            });
        }

        // DELETE: api/Suppliers/5
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            if (!await CanAccessSuppliersAsync())
                return Forbid();

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
            if (supplier == null)
                return NotFound(new { message = "Không tìm thấy nhà cung cấp!" });

            var hasBatches = await _context.ProductBatches.AnyAsync(b => b.SupplierId == id);
            if (hasBatches)
            {
                return BadRequest(new
                {
                    message = "Nhà vườn này đã có dữ liệu lô hàng nhập kho nên không thể xóa vĩnh viễn để đảm bảo truy xuất nguồn gốc. Hãy chuyển sang trạng thái Ngừng hợp tác!"
                });
            }

            _context.Suppliers.Remove(supplier);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã xóa nhà cung cấp “{supplier.Name}”."
            });
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeCertification(string? cert)
        {
            if (string.IsNullOrWhiteSpace(cert))
                return "Thông thường";

            var matched = AllowedCertifications.FirstOrDefault(
                c => string.Equals(c, cert.Trim(), StringComparison.OrdinalIgnoreCase));

            return matched ?? "Thông thường";
        }
    }
}
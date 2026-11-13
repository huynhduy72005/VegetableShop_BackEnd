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
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public ProductsController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        // GET: api/products (Lấy danh sách tất cả sản phẩm)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Product>>> GetProducts()
        {
            return await _context.Products.ToListAsync();
        }

        // GET: api/products/5 (Lấy chi tiết sản phẩm theo ID)
        [HttpGet("{id}")]
        public async Task<ActionResult<Product>> GetProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });
            }
            return product;
        }

        // POST: api/products (Thêm sản phẩm mới - dùng khi quản trị)
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Product>> PostProduct(Product product)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
        }

        // PUT: api/products/5 (Cập nhật sản phẩm)
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

            _context.Entry(product).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Products.Any(e => e.Id == id))
                {
                    return NotFound(new { message = "Không tìm thấy sản phẩm cần sửa!" });
                }
                else
                {
                    throw;
                }
            }

            return Ok(new { success = true, message = "Cập nhật sản phẩm thành công!" });
        }

        [HttpPut("{id}/inventory")]
        [Authorize]
        public async Task<IActionResult> UpdateInventory(
            int id,
            [FromBody] InventoryUpdateDto request)
        {
            if (!await _permissions.CanManageInventoryAsync(User))
                return Forbid();

            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });

            product.Quantity = request.Quantity!.Value;
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

            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm!" });
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa sản phẩm thành công!" });
        }
    }

    public sealed class InventoryUpdateDto
    {
        [Required]
        [Range(0, int.MaxValue)]
        public int? Quantity { get; set; }
    }
}
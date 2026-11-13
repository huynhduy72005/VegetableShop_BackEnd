using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

namespace VegetableShopAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly EmployeePermissionService _permissions;

        public CategoriesController(
            ApplicationDbContext context,
            EmployeePermissionService permissions)
        {
            _context = context;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Category>>> GetCategories() => await _context.Categories.ToListAsync();



        // THÊM DANH MỤC MỚI
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<Category>> PostCategory(Category category)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            // Xóa hoặc comment dòng cũ bị lỗi:
            // return CreatedAtAction("GetCategory", new { id = category.Id }, category);

            // SỬA THÀNH DÒNG NÀY:
            return Ok(category);
        }

        // SỬA DANH MỤC
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> PutCategory(int id, Category category)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            if (id != category.Id)
            {
                return BadRequest();
            }

            _context.Entry(category).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CategoryExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // XÓA DANH MỤC
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            if (!await _permissions.CanManageProductsAsync(User))
                return Forbid();

            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CategoryExists(int id)
        {
            return _context.Categories.Any(e => e.Id == id);
        }
    }
}
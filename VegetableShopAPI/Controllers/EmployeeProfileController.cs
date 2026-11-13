using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;

namespace VegetableShopAPI.Controllers;

[ApiController]
[Route("api/employee-profile")]
[Authorize(Roles = "Staff")]
public class EmployeeProfileController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public EmployeeProfileController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId))
        {
            return Unauthorized();
        }

        var account = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Username,
                user.FullName,
                user.Email,
                user.Phone,
                user.Role
            })
            .FirstOrDefaultAsync();

        if (account == null)
            return Unauthorized();

        var employee = await _db.Employees
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => new
            {
                profile.EmployeeCode,
                profile.Position,
                profile.Status,
                profile.CanManageProducts,
                profile.CanManageOrders,
                profile.CanManageInventory
            })
            .FirstOrDefaultAsync();

        return Ok(new { account, employee });
    }
}

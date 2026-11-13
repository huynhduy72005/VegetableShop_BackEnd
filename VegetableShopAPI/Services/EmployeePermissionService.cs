using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;

namespace VegetableShopAPI.Services;

public sealed class EmployeePermissionService
{
    private readonly ApplicationDbContext _db;

    public EmployeePermissionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> CanManageProductsAsync(ClaimsPrincipal user) =>
        HasPermissionAsync(user, EmployeePermission.Products);

    public Task<bool> CanManageOrdersAsync(ClaimsPrincipal user) =>
        HasPermissionAsync(user, EmployeePermission.Orders);

    public Task<bool> CanManageInventoryAsync(ClaimsPrincipal user) =>
        HasPermissionAsync(user, EmployeePermission.Inventory);

    private async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        EmployeePermission permission)
    {
        if (user.IsInRole("Admin"))
            return true;

        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return false;

        return permission switch
        {
            EmployeePermission.Products => await _db.Employees.AnyAsync(employee =>
                employee.UserId == userId &&
                employee.Status == "Active" &&
                employee.CanManageProducts),
            EmployeePermission.Orders => await _db.Employees.AnyAsync(employee =>
                employee.UserId == userId &&
                employee.Status == "Active" &&
                employee.CanManageOrders),
            EmployeePermission.Inventory => await _db.Employees.AnyAsync(employee =>
                employee.UserId == userId &&
                employee.Status == "Active" &&
                employee.CanManageInventory),
            _ => false
        };
    }

    private enum EmployeePermission
    {
        Products,
        Orders,
        Inventory
    }
}

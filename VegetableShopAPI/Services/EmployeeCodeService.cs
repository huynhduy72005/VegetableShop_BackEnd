using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;

namespace VegetableShopAPI.Services;

public class EmployeeCodeService
{
    private const int MaximumSequence = 99999;
    private readonly ApplicationDbContext _db;

    public EmployeeCodeService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<string> GenerateAsync(
        string role,
        int? excludeEmployeeId = null,
        string? existingCode = null)
    {
        var prefix = role switch
        {
            "Admin" => "AD",
            "Staff" => "NV",
            "Customer" => "KH",
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                "Vai trò tài khoản không có tiền tố mã phù hợp.")
        };

        if (TryGetSuffix(existingCode, out var currentSequence))
        {
            var preservedCode = $"{prefix}{currentSequence:D5}";
            if (!await _db.Employees.AnyAsync(employee =>
                employee.Id != excludeEmployeeId &&
                employee.EmployeeCode == preservedCode))
            {
                return preservedCode;
            }
        }

        var codes = await _db.Employees
            .AsNoTracking()
            .Where(employee =>
                employee.Id != excludeEmployeeId &&
                employee.EmployeeCode.StartsWith(prefix))
            .Select(employee => employee.EmployeeCode)
            .ToListAsync();
        var maximum = codes
            .Select(code => TryGetSuffix(code, out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max();

        if (maximum >= MaximumSequence)
            throw new InvalidOperationException(
                $"Đã dùng hết dãy mã nhân viên {prefix}.");

        return $"{prefix}{maximum + 1:D5}";
    }

    private static bool TryGetSuffix(string? code, out int sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(code) || code.Length <= 2)
            return false;

        if (!int.TryParse(
            code.AsSpan(2),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out sequence) ||
            sequence < 1 ||
            sequence > MaximumSequence)
        {
            sequence = 0;
            return false;
        }

        return true;
    }
}

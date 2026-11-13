using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Services;

public class AccountCredentialService
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<User> _passwordHasher;

    public AccountCredentialService(
        ApplicationDbContext db,
        IPasswordHasher<User> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public string HashPassword(User user, string password) =>
        _passwordHasher.HashPassword(user, password);

    public bool VerifyPassword(User user, string password, out bool hashUpgraded)
    {
        hashUpgraded = false;
        PasswordVerificationResult result;
        try
        {
            result = _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                password);
        }
        catch (FormatException)
        {
            result = PasswordVerificationResult.Failed;
        }

        if (result != PasswordVerificationResult.Failed)
        {
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = HashPassword(user, password);
                hashUpgraded = true;
            }

            return true;
        }

        if (!FixedTimeEquals(user.PasswordHash, password))
            return false;

        user.PasswordHash = HashPassword(user, password);
        hashUpgraded = true;
        return true;
    }

    public async Task<string> CreateUniqueUsernameAsync(
        string preferredUsername,
        int? excludeUserId = null)
    {
        var baseUsername = Regex.Replace(
            preferredUsername.Trim().ToLowerInvariant(),
            @"[^a-z0-9._-]",
            "");
        if (baseUsername.Length > 80)
            baseUsername = baseUsername[..80];
        if (string.IsNullOrWhiteSpace(baseUsername))
            baseUsername = "user";

        var candidate = baseUsername;
        var suffix = 2;
        while (await _db.Users.AnyAsync(user =>
            user.Id != excludeUserId &&
            user.Username.ToLower() == candidate))
        {
            var suffixText = $"_{suffix++}";
            candidate = baseUsername[..Math.Min(
                baseUsername.Length,
                80 - suffixText.Length)] + suffixText;
        }

        return candidate;
    }

    public static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%_-+";
        var all = upper + lower + digits + symbols;
        var password = new char[18];
        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (var index = 4; index < password.Length; index++)
            password[index] = all[RandomNumberGenerator.GetInt32(all.Length)];

        for (var index = password.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[swapIndex]) =
                (password[swapIndex], password[index]);
        }

        return new string(password);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

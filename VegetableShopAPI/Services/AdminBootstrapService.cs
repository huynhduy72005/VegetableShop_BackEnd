using System.Text;
using Microsoft.EntityFrameworkCore;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;

namespace VegetableShopAPI.Services;

public class AdminBootstrapService
{
    private const string VipEmail = "admin@gmail.com";
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly AccountCredentialService _credentials;

    public AdminBootstrapService(
        ApplicationDbContext db,
        IConfiguration configuration,
        AccountCredentialService credentials)
    {
        _db = db;
        _configuration = configuration;
        _credentials = credentials;
    }

    public async Task EnsureVipAdminAsync()
    {
        var password = _configuration["AdminBootstrap:Password"];
        if (string.IsNullOrWhiteSpace(password) ||
            Encoding.UTF8.GetByteCount(password) < 16)
        {
            throw new InvalidOperationException(
                "Hãy cấu hình AdminBootstrap:Password dài ít nhất 16 byte trong User Secrets.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(account =>
            account.Email.ToLower() == VipEmail);
        var conflictingVipAccount = await _db.Users.AnyAsync(account =>
            (account.IsVipAdmin || account.Role == "AdminVIP") &&
            account.Email.ToLower() != VipEmail);
        if (conflictingVipAccount)
        {
            throw new InvalidOperationException(
                "Chỉ tài khoản admin@gmail.com được phép có vai trò Quản trị viên VIP.");
        }

        if (user?.IsVipAdmin == true)
        {
            if (user.Role != "AdminVIP")
            {
                user.Role = "AdminVIP";
                await _db.SaveChangesAsync();
            }
            return;
        }

        if (user == null)
        {
            user = new User
            {
                Username = await _credentials.CreateUniqueUsernameAsync("admin"),
                FullName = "Quản trị viên VIP",
                Email = VipEmail,
                PasswordHash = string.Empty,
                Role = "AdminVIP",
                IsVipAdmin = true,
                IsActive = true
            };
            user.PasswordHash = _credentials.HashPassword(user, password);
            _db.Users.Add(user);
        }
        else
        {
            if (!_credentials.VerifyPassword(user, password, out _))
            {
                throw new InvalidOperationException(
                    "admin@gmail.com đã tồn tại. Chỉ có thể nâng tài khoản này thành Quản trị viên VIP khi AdminBootstrap:Password khớp mật khẩu hiện tại.");
            }

            user.Role = "AdminVIP";
            user.IsVipAdmin = true;
            user.IsActive = true;
        }

        await _db.SaveChangesAsync();
    }
}

public class AdminBootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminBootstrapHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var bootstrap = scope.ServiceProvider
            .GetRequiredService<AdminBootstrapService>();
        await bootstrap.EnsureVipAdminAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

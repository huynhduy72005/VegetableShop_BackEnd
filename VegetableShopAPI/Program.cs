//using Microsoft.EntityFrameworkCore;
//using VegetableShopAPI.Data;
//using VegetableShopAPI.Services;

//var builder = WebApplication.CreateBuilder(args);

//// ??ng k� k?t n?i SQL Server v?i Entity Framework Core
//builder.Services.AddDbContext<ApplicationDbContext>(options =>
//    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen();
//builder.Services.AddHttpClient();

//// Th�m ch�nh s�ch CORS cho ph�p Frontend g?i API
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("AllowReactApp",
//        policy =>
//        {
//            policy.AllowAnyOrigin()
//                  .AllowAnyHeader()
//                  .AllowAnyMethod();
//        });
//});

//builder.Services.AddScoped<IEmailService, EmailService>();

//var app = builder.Build();

//// K�ch ho?t CORS
//app.UseCors("AllowReactApp");

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();

//app.UseAuthorization();

//app.MapControllers();

//app.Run();

using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VegetableShopAPI.Data;
using VegetableShopAPI.Models;
using VegetableShopAPI.Services;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionPath = builder.Configuration["DataProtection:KeysPath"];
if (string.IsNullOrWhiteSpace(dataProtectionPath))
{
    dataProtectionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VegetableShopAPI",
        "DataProtection-Keys");
}
Directory.CreateDirectory(dataProtectionPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey) ||
    Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Hãy cấu hình Jwt:Key dài ít nhất 32 byte trong User Secrets.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AccountCredentialService>();
builder.Services.AddScoped<EmployeeCodeService>();
builder.Services.AddScoped<EmployeePermissionService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = "VegetableShopAPI",
            ValidAudience = "VegetableShopReact",

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var id = context.Principal?
                    .FindFirstValue(ClaimTypes.NameIdentifier);

                var role = context.Principal?
                    .FindFirstValue(ClaimTypes.Role);
                var passwordFingerprint = context.Principal?
                    .FindFirstValue("password_fingerprint");

                var db = context.HttpContext.RequestServices
                    .GetRequiredService<ApplicationDbContext>();

                if (!int.TryParse(id, out var userId) ||
                    string.IsNullOrEmpty(passwordFingerprint))
                {
                    context.Fail(
                        "Tài khoản hoặc quyền đã thay đổi. Hãy đăng nhập lại.");
                    return;
                }

                var user = await db.Users
                    .Where(account => account.Id == userId && account.Role == role)
                    .Select(account => new
                    {
                        account.PasswordHash,
                        account.IsActive
                    })
                    .FirstOrDefaultAsync();
                var currentFingerprint = user == null || !user.IsActive
                    ? Array.Empty<byte>()
                    : HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(jwtKey),
                        Encoding.UTF8.GetBytes(user.PasswordHash));
                byte[] tokenFingerprint;
                try
                {
                    tokenFingerprint = Convert.FromHexString(passwordFingerprint);
                }
                catch (FormatException)
                {
                    context.Fail("Phiên đăng nhập không hợp lệ. Hãy đăng nhập lại.");
                    return;
                }

                if (currentFingerprint.Length == 0 ||
                    !CryptographicOperations.FixedTimeEquals(
                        currentFingerprint,
                        tokenFingerprint))
                {
                    context.Fail(
                        "Mật khẩu tài khoản đã thay đổi. Hãy đăng nhập lại.");
                }

                var employeeIsInactive = await db.Employees
                    .AnyAsync(employee =>
                        employee.UserId == userId &&
                        employee.Status != "Active");
                if (employeeIsInactive)
                {
                    context.Fail(
                        "Tài khoản nhân viên đã bị vô hiệu hóa.");
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowReactApp");
app.UseStaticFiles();

app.UseAuthentication();
app.Use(async (context, next) =>
{
    var requiresPasswordChange = context.User.HasClaim(
        "must_change_password",
        "true");
    var isChangePasswordRequest =
        context.Request.Path.Value?.StartsWith(
            "/api/users/change-password/",
            StringComparison.OrdinalIgnoreCase) == true;

    if (requiresPasswordChange && !isChangePasswordRequest)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "PASSWORD_CHANGE_REQUIRED",
            message = "Bạn cần đổi mật khẩu trước khi sử dụng hệ thống."
        });
        return;
    }

    await next();
});
app.UseAuthorization();

app.MapControllers();

app.Run();
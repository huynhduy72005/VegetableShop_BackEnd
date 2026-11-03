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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VegetableShopAPI.Data;
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
                    .Select(account => account.Password)
                    .FirstOrDefaultAsync();
                var currentFingerprint = user == null
                    ? Array.Empty<byte>()
                    : HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(jwtKey),
                        Encoding.UTF8.GetBytes(user));
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
            }
        };
    });

builder.Services.AddAuthorization();

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
app.UseAuthorization();

app.MapControllers();

app.Run();
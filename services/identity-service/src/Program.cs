using System.Text;
using System.Threading.RateLimiting;
using BuildingBlocks.Authorization;
using BuildingBlocks.Events;
using IdentityService.Data;
using IdentityService.Events;
using IdentityService.Filters;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Đọc .env (tự tìm ngược lên các thư mục cha). Trong Docker, biến môi trường được compose truyền vào.
// NoClobber: biến môi trường THẬT luôn thắng giá trị trong .env (project test dựa vào điều này để ghi đè cấu hình).
try
{
    DotNetEnv.Env.NoClobber().TraversePath().Load();
}
catch (FileNotFoundException)
{
    // Không có file .env (Docker, CI): dùng biến môi trường thật.
}

var builder = WebApplication.CreateBuilder(args);

// ---------- Database (PostgreSQL) ----------
var connectionString =
    Environment.GetEnvironmentVariable("DB_CONNECTION") ??
    $"Host={Environment.GetEnvironmentVariable("DB_HOST")};" +
    $"Port={Environment.GetEnvironmentVariable("DB_PORT")};" +
    $"Database={Environment.GetEnvironmentVariable("DB_NAME")};" +
    $"Username={Environment.GetEnvironmentVariable("DB_USER")};" +
    $"Password={Environment.GetEnvironmentVariable("DB_PASSWORD")}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// ---------- Cấu hình tài khoản / email ----------
var accountSettings = AccountSettings.FromEnvironment();
builder.Services.AddSingleton(accountSettings);
builder.Services.AddSingleton(SmtpSettings.FromEnvironment());
builder.Services.AddSingleton<IEmailService, SmtpEmailService>();
builder.Services.AddSingleton<IAccountEmailService, AccountEmailService>();

// Lưu khóa Data Protection vào DB: token xác thực email / đặt lại mật khẩu không bị mất khi restart container
builder.Services.AddDataProtection()
    .SetApplicationName("identity-service")
    .PersistKeysToDbContext<AppDbContext>();

// Thời gian sống của token xác thực email / đặt lại mật khẩu
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
    o.TokenLifespan = TimeSpan.FromHours(accountSettings.TokenLifespanHours));

// ---------- Identity ----------
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddRoles<ApplicationRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

// ---------- JWT ----------
var jwt = JwtSettings.FromEnvironment();
builder.Services.AddSingleton(jwt);
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddHostedService<RefreshTokenCleanupService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name"
        };
    });

// ---------- Phân quyền theo Level: user được phép khi Level của user >= Level của permission ----------
// Level của permission nằm trong DB; cache trong bộ nhớ (TTL ngắn) và phát event khi thay đổi.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAccessLevelService, AccessLevelService>();
builder.Services.AddSingleton<IEventPublisher, LoggingEventPublisher>(); // thay bằng adapter broker thật khi đã chọn broker
builder.Services.AddSingleton<PermissionService>();
builder.Services.AddSingleton<IPermissionService>(sp => sp.GetRequiredService<PermissionService>());
builder.Services.AddSingleton<IPermissionLevelProvider>(sp => sp.GetRequiredService<PermissionService>());
builder.Services.AddPermissionAuthorization(); // AddAuthorization + handler + policy provider dùng chung (building-blocks)

// Endpoint nội bộ cho service khác (chỉ bật khi INTERNAL_API_KEY >= 32 ký tự)
builder.Services.AddSingleton(InternalApiSettings.FromEnvironment());
builder.Services.AddSingleton<InternalApiKeyFilter>();

// ---------- Rate limit cho các endpoint gửi email (chống spam/email bombing) ----------
var emailRateLimit =
    int.TryParse(Environment.GetEnvironmentVariable("EMAIL_RATE_LIMIT_PERMITS"), out var permits) && permits > 0
        ? permits
        : 10;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("email-send", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = emailRateLimit,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Tự động chạy migration khi khởi động (tiện cho dev/docker; production nên chạy migration riêng)
// Đặt AUTO_MIGRATE=false trong .env để tắt.
if (!string.Equals(Environment.GetEnvironmentVariable("AUTO_MIGRATE"), "false", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Đồng bộ danh mục permission từ code (chỉ thêm cái thiếu, không ghi đè Level trong DB).
// Luôn chạy, không phụ thuộc SEED_DATA: thiếu danh mục thì mọi [HasPermission] đều bị từ chối.
await IdentitySeeder.SyncPermissionsAsync(app.Services, app.Logger);

// Seed role (Admin, User) và tài khoản admin từ ADMIN_EMAIL/ADMIN_PASSWORD. Đặt SEED_DATA=false để tắt.
if (!string.Equals(Environment.GetEnvironmentVariable("SEED_DATA"), "false", StringComparison.OrdinalIgnoreCase))
{
    await IdentitySeeder.SeedAsync(app.Services, app.Logger);
}

// Giữ middleware xử lý lỗi hiện có của bạn, đặt ĐẦU pipeline:
// app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// Cho phép project test tham chiếu tới Program (WebApplicationFactory<Program>)
public partial class Program { }

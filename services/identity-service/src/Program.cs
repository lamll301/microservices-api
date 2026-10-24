using System.Text;
using IdentityService.Data;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Đọc .env (tự tìm ngược lên các thư mục cha). Trong Docker, biến môi trường được compose truyền vào.
DotNetEnv.Env.TraversePath().Load();

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
    .AddRoles<IdentityRole<Guid>>()
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

builder.Services.AddAuthorization();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

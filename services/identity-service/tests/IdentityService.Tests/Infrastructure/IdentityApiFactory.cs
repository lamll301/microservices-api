using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace IdentityService.Tests.Infrastructure;

public record TestUser(Guid Id, string Email, string Password);

/// <summary>
/// Dựng cả API trong bộ nhớ (TestServer) + một PostgreSQL thật chạy trong Docker.
/// Dùng chung cho mọi integration test (xem ApiCollection bên dưới) nên container chỉ khởi động một lần.
/// </summary>
public class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin@12345";
    public const string DefaultPassword = "Passw0rd123";
    public const string FrontendUrl = "http://localhost:3000";
    public const string InternalApiKey = "integration-test-internal-key-at-least-32-chars!!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    /// <summary>Hộp thư giả: test đọc email mà API "gửi" ở đây.</summary>
    public FakeEmailService Emails { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Program.cs đọc cấu hình từ biến môi trường, nên đặt chúng TRƯỚC khi host được dựng (lần đầu gọi CreateClient/Services).
        // Program.cs dùng NoClobber nên các giá trị này thắng mọi thứ trong file .env thật của bạn.
        var settings = new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = _postgres.GetConnectionString(),
            ["JWT_SECRET"] = "integration-test-secret-key-at-least-32-chars!!",
            ["JWT_ISSUER"] = "identity-service-tests",
            ["JWT_AUDIENCE"] = "microservices-api-tests",
            ["JWT_EXPIRE_MINUTES"] = "60",
            ["JWT_REFRESH_EXPIRE_DAYS"] = "7",
            ["INTERNAL_API_KEY"] = InternalApiKey,
            ["ADMIN_EMAIL"] = AdminEmail,
            ["ADMIN_PASSWORD"] = AdminPassword,
            ["SEED_DATA"] = "true",
            ["AUTO_MIGRATE"] = "true",               // chạy migration thật -> test cũng kiểm tra migration của bạn
            ["TOKEN_CLEANUP_ENABLED"] = "false",
            ["REQUIRE_CONFIRMED_EMAIL"] = "true",
            ["EMAIL_RATE_LIMIT_PERMITS"] = "100000", // tránh bị rate limit khi test gọi liên tục từ một IP
            ["API_PUBLIC_URL"] = "http://localhost",
            ["FRONTEND_URL"] = FrontendUrl
        };

        foreach (var (key, value) in settings)
            Environment.SetEnvironmentVariable(key, value);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Thay SMTP thật bằng hộp thư giả
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // ---------- helper dùng chung ----------

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    /// <summary>Tạo user thẳng qua UserManager (nhanh, không gửi email). Mặc định đã xác thực email và có role User.</summary>
    public async Task<TestUser> CreateUserAsync(bool emailConfirmed = true, string password = DefaultPassword, string role = AppRoles.User)
    {
        var email = NewEmail();

        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = "Test User",
            EmailConfirmed = emailConfirmed
        };

        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);

        return new TestUser(user.Id, email, password);
    }

    /// <summary>HttpClient đã gắn sẵn header Authorization: Bearer ...</summary>
    public HttpClient CreateAuthedClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

/// <summary>
/// Mọi test class có [Collection("Api")] dùng CHUNG một IdentityApiFactory (một container Postgres cho cả bộ test).
/// Vì cùng một collection nên chúng chạy tuần tự; mỗi test dùng email ngẫu nhiên để không đụng dữ liệu của nhau.
/// </summary>
[CollectionDefinition(ApiCollection.Name)]
public class ApiCollection : ICollectionFixture<IdentityApiFactory>
{
    public const string Name = "Api";
}

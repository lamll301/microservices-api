# CLAUDE.md — identity-service

Hướng dẫn cho Claude khi làm việc trong thư mục `services/identity-service` của repo `microservices-api`.

## Tổng quan

`identity-service` chịu trách nhiệm xác thực và phân quyền cho toàn hệ thống microservices: đăng ký, đăng nhập, cấp JWT, refresh token, xác thực email, quên/đặt lại/đổi mật khẩu, quản lý role.

- Ngôn ngữ/Framework: C# trên .NET 10, ASP.NET Core Web API (dùng Controllers, không dùng minimal API cho nghiệp vụ)
- Database: PostgreSQL qua EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- Identity: ASP.NET Core Identity với khóa chính `Guid` (`ApplicationUser : IdentityUser<Guid>`, `IdentityRole<Guid>`)
- Token: JWT access token (HS256) + refresh token ngẫu nhiên lưu dạng hash trong DB
- Email: MailKit gửi SMTP; môi trường dev dùng Mailpit
- Cấu hình: biến môi trường, nạp từ `.env` bằng `DotNetEnv` (không dùng `appsettings.json` cho secret)
- Chạy bằng Docker Compose (`postgres`, `mailpit`, `identity-service`)

## Cấu trúc thư mục

```
identity-service/
├── docker-compose.yml        # postgres + mailpit + identity-service
├── .env.example              # mẫu biến môi trường (commit); .env thật KHÔNG commit
├── CLAUDE.md
├── tests/
│   └── IdentityService.Tests/    # xUnit: Unit/ (không cần Docker), Integration/ (Testcontainers + Postgres thật)
└── src/
    ├── Dockerfile
    ├── IdentityService.Api.csproj
    ├── Program.cs            # đăng ký DI, Identity, JWT, rate limit, migrate + seed khi khởi động
    ├── Controllers/          # AuthController (/api/auth), AdminController (/api/admin), InternalController (/api/internal)
    ├── Data/                 # AppDbContext, IdentitySeeder
    ├── DTOs/                 # request/response (RegisterRequest, LoginRequest, AuthResponse, ...)
    ├── Entities/             # entity không thuộc Identity (RefreshToken, Permission, Department)
    ├── Events/               # LoggingEventPublisher (hợp đồng IEventPublisher nằm ở building-blocks)
    ├── Filters/              # InternalApiKeyFilter (bảo vệ endpoint nội bộ bằng X-Internal-Key)
    ├── Models/               # ApplicationUser, ApplicationRole, AppRoles, AccessLevels, AppPermissions
    ├── Middleware/           # ExceptionHandlingMiddleware
    ├── Repositories/         # (dự phòng)
    └── Services/             # TokenService, JwtSettings, EmailService, AccountEmailService,
                              # AccountSettings, TokenCodec, RefreshTokenCleanupService,
                              # PermissionService, PermissionSnapshot, AccessLevelService, InternalApiSettings
```

Cấu trúc repo (đường dẫn tương đối từ gốc `microservices-api/`):

```
building-blocks/                      # code dùng chung, xem building-blocks/README.md
├── BuildingBlocks.Authorization/     # [HasPermission], PermissionHandler, PermissionPolicyProvider,
│                                     # IPermissionLevelProvider, AuthClaimTypes, AddPermissionAuthorization()
└── BuildingBlocks.Events/            # IEventPublisher, PermissionLevelChangedEvent
services/
└── identity-service/                 # (cây thư mục ở trên)
```

`IdentityService.Api.csproj` tham chiếu hai project trên bằng `ProjectReference` (`..\..\..\building-blocks\...`). Namespace dùng chung bắt đầu bằng `BuildingBlocks.*`.

Namespace gốc là `IdentityService.*` (ví dụ `IdentityService.Services`, `IdentityService.DTOs`). Namespace phải khớp với thư mục.

## Lệnh thường dùng

Chạy trong thư mục `src/` trừ khi ghi chú khác.

```bash
dotnet restore
dotnet build
dotnet run                                   # chạy local (cần postgres đang chạy, DB_HOST=localhost)

dotnet ef migrations add <TenMigration>      # tạo migration sau khi đổi model/DbContext
dotnet ef database update                    # áp migration thủ công (mặc định app tự migrate khi khởi động)
dotnet ef migrations remove                  # bỏ migration cuối (chỉ khi chưa áp vào DB dùng chung)
```

Chạy bằng Docker (trong thư mục `identity-service/`, ngang hàng `docker-compose.yml`). Build context của image là THƯ MỤC GỐC REPO (`../..`) để Docker thấy `building-blocks/`; `Dockerfile` phải `COPY` cả `building-blocks/` lẫn `services/identity-service/src/`:

```bash
docker compose up -d --build                 # dựng và chạy toàn bộ
docker compose up -d postgres mailpit        # chỉ chạy hạ tầng, rồi dotnet run ngoài docker
docker compose logs -f identity-service
docker compose down                          # giữ dữ liệu; thêm -v để xóa cả volume DB
```

Địa chỉ khi chạy:
- API: `http://localhost:5137` (container lắng nghe 8080)
- Health check: `GET /health`
- Mailpit (xem email dev): `http://localhost:8025`
- PostgreSQL: `localhost:5432`

## Biến môi trường

Danh sách đầy đủ và giá trị mẫu nằm trong `.env.example`. Nhóm chính:

- `DB_HOST`, `DB_PORT`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` (hoặc một chuỗi `DB_CONNECTION`)
- `JWT_SECRET` (bắt buộc, tối thiểu 32 ký tự), `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_EXPIRE_MINUTES`, `JWT_REFRESH_EXPIRE_DAYS`
- `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `ADMIN_FULLNAME`, `SEED_DATA`
- `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASSWORD`, `SMTP_FROM_EMAIL`, `SMTP_FROM_NAME`
- `INTERNAL_API_KEY` (tối thiểu 32 ký tự; bật endpoint `/api/internal/*` cho service khác, để trống thì endpoint tắt)
- `API_PUBLIC_URL`, `FRONTEND_URL`, `REQUIRE_CONFIRMED_EMAIL`, `EMAIL_TOKEN_LIFESPAN_HOURS`, `EMAIL_RATE_LIMIT_PERMITS`
- `TOKEN_CLEANUP_ENABLED`, `TOKEN_CLEANUP_INTERVAL_HOURS`, `TOKEN_CLEANUP_RETENTION_DAYS`
- `AUTO_MIGRATE`, `ASPNETCORE_ENVIRONMENT`

Khi chạy bằng Docker Compose, `DB_HOST` luôn là `postgres` và `SMTP_HOST` là `mailpit` (đặt cứng trong `docker-compose.yml`); khi chạy `dotnet run` ngoài docker thì dùng `localhost`.

Khi thêm biến mới: cập nhật đồng thời `.env.example`, phần `environment` của `docker-compose.yml` và class cấu hình tương ứng (`JwtSettings`, `SmtpSettings`, `AccountSettings`).

## Test

Project test nằm ở `tests/IdentityService.Tests/` (xUnit 2 + `Microsoft.AspNetCore.Mvc.Testing` + Testcontainers).

- `Unit/`: test logic thuần (`TokenCodec`, `TokenService`), không cần Docker.
- `Integration/`: gọi API thật qua `WebApplicationFactory<Program>` + PostgreSQL thật trong Docker. **Cần Docker đang chạy.** Các class gắn `[Trait("Category", "Integration")]` và `[Collection("Api")]` để dùng chung một container.
- `Infrastructure/`: `IdentityApiFactory` (đặt biến môi trường rồi dựng host, thay SMTP bằng `FakeEmailService`), helper HTTP/email.

```bash
cd tests/IdentityService.Tests
dotnet test                                      # chạy tất cả (cần Docker)
dotnet test --filter "Category!=Integration"     # chỉ unit test, không cần Docker
dotnet test --filter "FullyQualifiedName~PasswordTests"   # một class
```

Quy tắc khi viết test:
- Mỗi test dùng email ngẫu nhiên (`IdentityApiFactory.NewEmail()` / `CreateUserAsync()`), không dựa vào dữ liệu của test khác và không sửa tài khoản admin được seed.
- Dùng `CreateUserAsync()` để dựng user nhanh; chỉ đi qua `/register` khi chính luồng đăng ký là thứ cần test.
- Đọc link xác thực/đặt lại mật khẩu từ `factory.Emails` (hộp thư giả), không gửi SMTP thật.
- Dùng `await response.ShouldBeAsync(HttpStatusCode.X)` để khi sai sẽ in kèm body lỗi của API.
- Thêm endpoint/luồng mới thì thêm test tương ứng; sửa lỗi thì viết test tái hiện lỗi trước.
- `Program.cs` phải giữ `public partial class Program { }` và `DotNetEnv.Env.NoClobber()`; hai thứ này là điều kiện để test chạy.

### CI (GitHub Actions)

Workflow ở `.github/workflows/identity-service-ci.yml` (thư mục gốc repo). Mỗi lần push, hoặc mở/cập nhật PR vào `main`, có thay đổi trong `services/identity-service/**` hoặc `building-blocks/**` thì chạy:
- Job `Build & Test`: restore, build Release, unit test rồi integration test (runner Ubuntu có sẵn Docker nên Testcontainers chạy được). Kết quả `.trx` được upload thành artifact `test-results`.
- Job `Docker image build`: build thử `src/Dockerfile` để chắc chắn image vẫn dựng được.

Lưu ý: thư mục `src/Migrations/` phải được commit, vì CI không có DB sẵn và test dựa vào migration để tạo bảng. Không commit `.env`. CI không cần secret nào vì test tự đặt cấu hình riêng.

## Các endpoint

`/api/auth` (công khai trừ khi ghi chú):
- `POST register`, `POST login`
- `POST refresh`, `POST logout`, `POST logout-all` (cần Bearer), `GET me` (cần Bearer)
- `GET confirm-email`, `POST resend-confirmation`
- `POST forgot-password`, `POST reset-password`, `POST change-password` (cần Bearer)

`/api/admin` (kiểm tra bằng `[HasPermission]`, xem mục Phân quyền theo Level):
- `GET users` (`identity.users.read`), `GET roles` (`identity.roles.read`)
- `POST users/{id}/roles`, `DELETE users/{id}/roles/{role}` (`identity.roles.assign`)
- `GET permissions` (`identity.permissions.read`), `PUT permissions/{code}/level` (`identity.permissions.manage`)

`/api/internal` (service-to-service, header `X-Internal-Key` = `INTERNAL_API_KEY`, không đưa ra gateway công khai):
- `GET permissions`: snapshot `{ version, items }` để service khác nạp cache.

Role hiện có (hằng số + Level mặc định trong `Models/AppRoles.cs`, `Models/AccessLevels.cs`): `SuperAdmin` 9999, `Admin` 9998, `Manager` 899, `User` 100. User đăng ký mới tự có role `User`.

## Phân quyền theo Level

- Mỗi role có `Level` (cột trong `AspNetRoles`); Level của user = Level cao nhất trong các role. Token chứa một claim `level`.
- Mỗi permission (bảng `Permissions`, tên `{service}.{resource}.{action}`) có `Level` tối thiểu. User được phép khi `level` của user >= `Level` của permission. Thiếu claim / claim sai / permission chưa khai báo thì từ chối.
- Controller dùng `[HasPermission(AppPermissions.Identity.X)]`, không dùng `[Authorize(Roles = ...)]`. Thêm permission mới: khai báo trong `AppPermissions.Definitions`; seeder tự thêm vào DB. Seeder KHÔNG ghi đè Level đã có trong DB; Level sửa lúc chạy qua `PUT /api/admin/permissions/{code}/level`.
- Cache + event: `PermissionService` cache snapshot trong bộ nhớ (TTL 60 giây) và xóa cache khi Level đổi, đồng thời phát `PermissionLevelChangedEvent` qua `IEventPublisher`. Event chỉ là tín hiệu làm mới cache; consumer phải tải lại snapshot (so sánh `version`) và vẫn làm mới theo TTL phòng mất event. Hiện `IEventPublisher` mới chỉ ghi log, chưa nối broker.
- Chống leo thang đặc quyền: chỉ được gán/gỡ role và thao tác trên user có Level THẤP HƠN HẲN cấp của mình; không tự gỡ role của mình; chỉ được đổi Level của permission đang thấp hơn cấp của mình và không đặt cao hơn cấp của mình. Vì vậy `SuperAdmin` không thể gán/gỡ qua API (chỉ qua DB). Level của người thao tác được đọc từ DB, không tin claim.
- Bảng `Departments` đã tạo nhưng chưa gắn với user/permission.

### building-blocks (đã tách)

- Dùng chung (ở `building-blocks/`): `[HasPermission]`, `PermissionHandler`, `PermissionPolicyProvider`, `IPermissionLevelProvider`, `AuthClaimTypes`, `AddPermissionAuthorization()`, `IEventPublisher`, `PermissionLevelChangedEvent`.
- Ở lại identity-service vì phụ thuộc DB/cấu hình riêng: `PermissionService` (cài đặt `IPermissionLevelProvider` bằng DB + cache), `LoggingEventPublisher`, `InternalApiKeyFilter`, `InternalApiSettings`.
- Không đặt logic nghiệp vụ, DbContext hay cấu hình riêng của một service vào `building-blocks/`. Đổi tên/chữ ký trong đó phải cập nhật mọi service cùng lúc.
- Service mới cần kiểm tra permission: tham chiếu `BuildingBlocks.Authorization`, gọi `AddPermissionAuthorization()`, tự đăng ký một `IPermissionLevelProvider` (cache snapshot TTL ngắn lấy từ `GET /api/internal/permissions` bằng `X-Internal-Key`, làm mới khi nhận `PermissionLevelChangedEvent` bằng cách so sánh `version`) và dùng chung `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`.

## Quy ước code

- Đặt tên, comment và thông báo lỗi trả về cho client bằng tiếng Việt, giống phần code hiện có; tên class/method/biến bằng tiếng Anh.
- Dùng `Nullable` đã bật: không bỏ qua cảnh báo null, dùng `string?` khi giá trị có thể null.
- Mọi thời gian lưu DB là UTC: dùng `DateTime.UtcNow`, tên thuộc tính kết thúc bằng `Utc`.
- Controller giữ mỏng; logic dùng chung (tạo token, gửi email) đặt trong `Services/` và đăng ký qua DI trong `Program.cs`.
- Request/response dùng DTO trong `DTOs/`, có validation bằng DataAnnotations. Không trả entity hoặc `ApplicationUser` trực tiếp ra ngoài.
- Lỗi trả về dạng JSON `{ "message": "..." }` hoặc `{ "errors": [{ "code", "description" }] }` (khi lỗi đến từ Identity).
- Không dùng `async void`; method bất đồng bộ trả `Task`/`Task<T>`.
- Thay đổi schema luôn đi kèm migration; không sửa tay file migration đã được áp vào DB dùng chung.

## Quy tắc bảo mật (không được vi phạm)

- Không commit `.env`, mật khẩu, `JWT_SECRET` hay khóa thật. Chỉ commit `.env.example`.
- Refresh token chỉ lưu hash SHA-256 trong DB; token gốc chỉ trả cho client một lần.
- Giữ cơ chế xoay vòng refresh token và phát hiện dùng lại token cũ (thu hồi toàn bộ phiên của user). Chỉ áp dụng cho token đã bị xoay vòng (có `ReplacedByTokenHash`); token bị thu hồi do logout/đổi mật khẩu chỉ bị từ chối, không kéo sập các phiên khác.
- Các endpoint `forgot-password` và `resend-confirmation` luôn trả cùng một thông báo, không để lộ email có tồn tại hay không.
- Thông báo đăng nhập sai luôn chung chung ("Email hoặc mật khẩu không đúng"); chỉ báo "chưa xác thực email" sau khi mật khẩu đúng.
- Các endpoint gửi email phải giữ rate limit `email-send`.
- Token xác thực email / đặt lại mật khẩu đi qua `TokenCodec` (Base64Url) khi đưa vào URL.
- Không log mật khẩu, token hay nội dung link xác thực/đặt lại mật khẩu.

## Lưu ý đã biết

- `.env` được nạp bằng `NoClobber()`: biến môi trường thật luôn thắng giá trị trong `.env`. Project test dựa vào điều này để ghi đè cấu hình.
- Khóa Data Protection được lưu trong DB (bảng `DataProtectionKeys`) để token email không mất khi restart container; không bỏ cấu hình này khi chạy nhiều instance.
- Role và `level` nằm trong access token, nên đổi role hoặc Level của role chỉ có hiệu lực sau khi access token hết hạn hoặc sau khi gọi `/refresh`. Đổi Level của permission thì có hiệu lực ngay (sau khi cache làm mới) vì Level permission không nằm trong token.
- `IEventPublisher` hiện là `LoggingEventPublisher` (chỉ ghi log), chưa nối message broker. Chọn broker xong thì thay bản đăng ký trong `Program.cs`; nhớ cân nhắc outbox nếu cần đảm bảo không mất event.
- Thay đổi trong `building-blocks/` kích hoạt CI của mọi service tham chiếu nó (workflow đã theo dõi `building-blocks/**`).
- Chạy nhiều instance identity-service: cache permission của instance khác chỉ làm mới theo TTL 60 giây (event chỉ báo cho service khác).
- `SEED_DATA=false` tắt seed role/admin: khi đó phải tự bảo đảm `AspNetRoles.Level` đã có giá trị, nếu không mọi user có Level 0 và bị từ chối.
- `REQUIRE_CONFIRMED_EMAIL=true` khiến user cũ có `EmailConfirmed = false` không đăng nhập được. Khi dev có thể chạy `UPDATE "AspNetUsers" SET "EmailConfirmed" = TRUE;`.
- Dòng `app.UseMiddleware<ExceptionHandlingMiddleware>();` trong `Program.cs` được comment sẵn vì không biết namespace thật của middleware; bỏ comment và thêm `using` đúng.
- `Program.cs` gọi `AddOpenApi()`; cần package `Microsoft.AspNetCore.OpenApi` (có sẵn trong template webapi).
- Sau reverse proxy/gateway, rate limit theo IP sẽ thấy IP của proxy; cần cấu hình `ForwardedHeaders`.
- Service khác muốn xác thực JWT phải dùng chung `JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE` với service này.

## Khi làm việc với Claude

- Trước khi sửa, đọc file liên quan; giữ phong cách và namespace hiện có.
- Sau khi đổi code, chạy `dotnet build` để kiểm tra biên dịch; nếu đổi model/DbContext thì nhắc tạo migration.
- Không tự ý đổi thư viện, phiên bản .NET hay kiến trúc khi chưa được yêu cầu.
- Với thay đổi liên quan bảo mật (auth, token, mật khẩu, role), nêu rõ tác động trước khi thực hiện.

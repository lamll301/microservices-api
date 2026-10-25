# building-blocks

Thư viện dùng chung cho các service trong `microservices-api`. Các service tham chiếu bằng `ProjectReference` (không đóng gói NuGet).

| Project | Nội dung |
|---|---|
| `BuildingBlocks.Authorization` | `[HasPermission]`, `PermissionHandler`, policy động, `IPermissionLevelProvider`, `AddPermissionAuthorization()` |
| `BuildingBlocks.Events` | `IEventPublisher` và các integration event (`PermissionLevelChangedEvent`) |

Quy tắc:
- Chỉ đặt hợp đồng (interface, event, attribute, handler) dùng chung. Không đặt logic nghiệp vụ, DbContext hay cấu hình riêng của một service.
- Mọi thay đổi ở đây ảnh hưởng tới tất cả service: giữ tương thích ngược; đổi tên/chữ ký phải cập nhật mọi service cùng lúc.
- Namespace bắt đầu bằng `BuildingBlocks.` và khớp với thư mục. `BuildingBlocks.Events` không phụ thuộc ASP.NET.
- Service dùng phân quyền cần: `AddPermissionAuthorization()` + tự đăng ký một `IPermissionLevelProvider` (identity-service đọc DB; service khác cache snapshot từ `GET /api/internal/permissions`).

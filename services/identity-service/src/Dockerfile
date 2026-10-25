FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1. Copy các building-blocks trước vì IdentityService phụ thuộc vào chúng
COPY building-blocks/ building-blocks/

# 2. Copy file csproj của identity service để restore trước nhằm tận dụng cache layer
COPY services/identity-service/src/IdentityService.Api/IdentityService.Api.csproj services/identity-service/src/IdentityService.Api/
RUN dotnet restore services/identity-service/src/IdentityService.Api/IdentityService.Api.csproj

# 3. Copy toàn bộ source code còn lại của identity service và building-blocks
COPY services/identity-service/src/ services/identity-service/src/

# 4. Publish project
RUN dotnet publish services/identity-service/src/IdentityService.Api/IdentityService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "IdentityService.Api.dll"]
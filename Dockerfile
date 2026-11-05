# Build from workspace parent directory so sibling ../Davids.Shared is in build context:
#   docker build -f Davids.Services.Products/Dockerfile -t ghcr.io/davids/davids-services-products:1.0.0 .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /workspace

COPY Davids.Shared/Directory.Build.props Davids.Shared/Directory.Packages.props Davids.Shared/global.json ./Davids.Shared/
COPY Davids.Shared/eng/ ./Davids.Shared/eng/
COPY Davids.Shared/src/Davids.ServiceDefaults/Davids.ServiceDefaults.csproj Davids.Shared/src/Davids.ServiceDefaults/packages.lock.json ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Products/Directory.Build.props Davids.Services.Products/Directory.Packages.props Davids.Services.Products/global.json ./Davids.Services.Products/
COPY Davids.Services.Products/src/Davids.Services.Products.Core/Davids.Services.Products.Core.csproj Davids.Services.Products/src/Davids.Services.Products.Core/packages.lock.json ./Davids.Services.Products/src/Davids.Services.Products.Core/
COPY Davids.Services.Products/src/Davids.Services.Products.Application/Davids.Services.Products.Application.csproj Davids.Services.Products/src/Davids.Services.Products.Application/packages.lock.json ./Davids.Services.Products/src/Davids.Services.Products.Application/
COPY Davids.Services.Products/src/Davids.Services.Products.Infrastructure/Davids.Services.Products.Infrastructure.csproj Davids.Services.Products/src/Davids.Services.Products.Infrastructure/packages.lock.json ./Davids.Services.Products/src/Davids.Services.Products.Infrastructure/
COPY Davids.Services.Products/src/Davids.Services.Products.Api/Davids.Services.Products.Api.csproj Davids.Services.Products/src/Davids.Services.Products.Api/packages.lock.json ./Davids.Services.Products/src/Davids.Services.Products.Api/

RUN dotnet restore ./Davids.Services.Products/src/Davids.Services.Products.Api/Davids.Services.Products.Api.csproj --locked-mode

COPY Davids.Shared/src/Davids.ServiceDefaults/ ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Products/src/ ./Davids.Services.Products/src/

RUN dotnet publish ./Davids.Services.Products/src/Davids.Services.Products.Api/Davids.Services.Products.Api.csproj \
      --no-restore -c Release /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:10001 /workspace/Davids.Services.Products/artifacts/publish/Davids.Services.Products.Api/release .
USER 10001:10001
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Davids.Services.Products.Api.dll"]

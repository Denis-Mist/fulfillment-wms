# syntax=docker/dockerfile:1
# Образ API Fulfillment WMS (ASP.NET Core, .NET 10). Контекст сборки — каталог backend/.
#   docker build -t wms-api backend
# Сборка рассчитана на слабый сервер (2 ГБ RAM без swap): один процесс MSBuild, без shared compilation server.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
WORKDIR /src
# Сначала только проекты — слой restore кешируется, пока не меняются зависимости
COPY global.json ./
COPY src/Wms.Domain/Wms.Domain.csproj src/Wms.Domain/
COPY src/Wms.Application/Wms.Application.csproj src/Wms.Application/
COPY src/Wms.Infrastructure/Wms.Infrastructure.csproj src/Wms.Infrastructure/
COPY src/Wms.Api/Wms.Api.csproj src/Wms.Api/
RUN --mount=type=cache,id=wms-nuget,target=/root/.nuget/packages \
    dotnet restore src/Wms.Api/Wms.Api.csproj
COPY src/ src/
RUN --mount=type=cache,id=wms-nuget,target=/root/.nuget/packages \
    dotnet publish src/Wms.Api/Wms.Api.csproj -c Release --no-restore -o /out \
      -p:UseAppHost=false -p:UseSharedCompilation=false -m:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
LABEL org.opencontainers.image.title="wms-api" \
      org.opencontainers.image.source="https://github.com/Denis-Mist/fulfillment-wms" \
      wms.component="api"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_NOLOGO=1
WORKDIR /app
COPY --from=build /out ./
# непривилегированный пользователь из базового образа
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Wms.Api.dll"]

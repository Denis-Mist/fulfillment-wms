#!/usr/bin/env bash
# Сборка артефактов на машине сборки (нужен только Docker): self-contained API для linux-x64 и статика фронта.
# Результат: deploy/out/api/ и deploy/out/web/
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/deploy/out"
rm -rf "$OUT" && mkdir -p "$OUT/api" "$OUT/web"

docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -v "$ROOT/backend:/src" -v "$OUT/api:/out" -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet publish src/Wms.Api/Wms.Api.csproj -c Release -r linux-x64 --self-contained true \
    -p:InvariantGlobalization=false -o /out

docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -e VITE_USE_MOCKS=false -e VITE_API_BASE_URL=/api \
  -v "$ROOT/frontend:/src" -w /src node:22-alpine \
  sh -c 'npm ci --no-audit --no-fund && npm run build'
cp -r "$ROOT/frontend/dist/." "$OUT/web/"
rm -f "$OUT/web/mockServiceWorker.js"
echo "OK: $OUT/api, $OUT/web ($(git -C "$ROOT" rev-parse --short HEAD))"

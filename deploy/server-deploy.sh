#!/usr/bin/env bash
# Деплой WMS из клона /opt/wms/src (запускается через /usr/local/sbin/wms-deploy, от root, под блокировкой).
#  1. сборка образа API (docker buildx, отдельный builder "wms-builder" с лимитом памяти — сборка не может съесть
#     память у соседних сервисов на сервере);
#  2. сборка фронта в одноразовом node-контейнере (с лимитом памяти) в /opt/wms/releases/web-<sha>;
#  3. pg_dump в /opt/wms/backups;
#  4. docker compose up -d --wait (проект wms: postgres + api); при провале — откат API на предыдущий образ;
#  5. атомарное переключение симлинка /opt/wms/web на новый фронт (только если API здоров);
#  6. уборка: 3 последних релиза фронта, 3 последних образа API, 10 последних дампов.
# Скрипт трогает только объекты WMS: контейнеры wms-*, том wms-pgdata, сеть wms-net, образы wms-api, builder wms-builder.
set -euo pipefail

APP="${WMS_APP:-/opt/wms}"
SRC="$APP/src"
ENV_FILE="$APP/.env"
COMPOSE=(docker compose -p wms -f "$SRC/deploy/docker-compose.yml" --env-file "$ENV_FILE")
BUILDER=wms-builder
BUILD_MEM="${WMS_BUILD_MEM:-1100m}"     # лимит памяти сборки (сервер 2 ГБ без swap)
SHA="$(git -C "$SRC" rev-parse --short=12 HEAD)"
TS="$(date +%Y%m%d%H%M%S)"
log() { printf '\n==> [%s] %s\n' "$(date +%T)" "$*"; }

[ -f "$ENV_FILE" ] || { echo "нет $ENV_FILE (см. deploy/server-setup.sh)" >&2; exit 1; }
API_PORT="$(sed -n 's/^WMS_API_PORT=//p' "$ENV_FILE")"; API_PORT="${API_PORT:-5080}"
install -d -m 755 "$APP/releases" "$APP/state"
install -d -m 700 "$APP/backups"

# ---------- 1. образ API ----------
log "Сборка образа API ($SHA)"
if ! docker buildx inspect "$BUILDER" >/dev/null 2>&1; then
  docker buildx create --name "$BUILDER" --driver docker-container \
    --driver-opt "memory=$BUILD_MEM" --driver-opt "memory-swap=$BUILD_MEM" >/dev/null
fi
docker buildx build --builder "$BUILDER" --progress=plain --load \
  -t "wms-api:$SHA" "$SRC/backend"

# ---------- 2. фронт ----------
WEB_DIR="$APP/releases/web-$SHA"
log "Сборка фронта -> $WEB_DIR"
rm -rf "$WEB_DIR.tmp" && mkdir -p "$WEB_DIR.tmp"
docker run --rm --name "wms-web-build-$TS" --memory "$BUILD_MEM" --memory-swap "$BUILD_MEM" \
  -e VITE_USE_MOCKS=false -e VITE_API_BASE_URL=/api \
  -e NODE_OPTIONS=--max-old-space-size=768 -e PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 \
  -e npm_config_update_notifier=false \
  -v "$SRC/frontend:/in:ro" -v "$WEB_DIR.tmp:/out" -v wms-npm-cache:/root/.npm \
  node:22-alpine sh -euc '
    mkdir /work && cp -a /in/. /work/ && cd /work
    npm ci --no-audit --no-fund --loglevel=error
    npx vite build
    cp -a dist/. /out/ && rm -f /out/mockServiceWorker.js'
[ -s "$WEB_DIR.tmp/index.html" ] || { echo "сборка фронта не дала index.html" >&2; exit 1; }
chmod -R a+rX "$WEB_DIR.tmp"
rm -rf "$WEB_DIR" && mv "$WEB_DIR.tmp" "$WEB_DIR"

# ---------- 3. бэкап БД ----------
if [ "$(docker inspect -f '{{.State.Health.Status}}' wms-postgres 2>/dev/null)" = healthy ]; then
  log "pg_dump -> $APP/backups/wms-$TS-$SHA.dump"
  docker exec wms-postgres pg_dump -U wms -d wms -Fc > "$APP/backups/wms-$TS-$SHA.dump.tmp"
  mv "$APP/backups/wms-$TS-$SHA.dump.tmp" "$APP/backups/wms-$TS-$SHA.dump"
  chmod 600 "$APP/backups/wms-$TS-$SHA.dump"
fi

# ---------- 4. переключение API ----------
# миграция со старой схемы (API как systemd-сервис): освобождаем порт перед запуском контейнера
if systemctl list-unit-files wms-api.service >/dev/null 2>&1 && systemctl cat wms-api.service >/dev/null 2>&1; then
  log "Отключаю legacy systemd-сервис wms-api"
  systemctl disable --now wms-api.service || true
fi

PREV_IMAGE="$(docker image inspect -f '{{.Id}}' wms-api:latest 2>/dev/null || true)"
docker tag "wms-api:$SHA" wms-api:latest
log "docker compose up (wms-api:$SHA)"
if ! "${COMPOSE[@]}" up -d --wait --wait-timeout 240 --remove-orphans; then
  echo "!! API не стал healthy" >&2
  "${COMPOSE[@]}" logs --tail 80 api >&2 || true
  if [ -n "$PREV_IMAGE" ]; then
    log "Откат API на предыдущий образ"
    docker tag "$PREV_IMAGE" wms-api:latest
    "${COMPOSE[@]}" up -d --wait --wait-timeout 240 api || true
  fi
  echo "Деплой $SHA провален; фронт не переключён" >&2
  exit 1
fi
curl -fsS --retry 10 --retry-delay 3 --retry-all-errors "http://127.0.0.1:$API_PORT/health" >/dev/null
echo "API healthy: http://127.0.0.1:$API_PORT/health"

# ---------- 5. переключение фронта (атомарно) ----------
log "Переключаю /opt/wms/web -> $WEB_DIR"
if [ -d "$APP/web" ] && [ ! -L "$APP/web" ]; then
  mv "$APP/web" "$APP/releases/web-legacy-$TS"      # первая миграция: каталог -> симлинк
fi
ln -sfn "$WEB_DIR" "$APP/web.new" && mv -T "$APP/web.new" "$APP/web"
echo "$SHA" > "$APP/state/deployed-sha"

# legacy-артефакты старой схемы
[ -f "$APP/docker-compose.db.yml" ] && mv "$APP/docker-compose.db.yml" "$APP/backups/legacy-docker-compose.db.yml.$TS"
[ -d "$APP/api" ] && rm -rf "$APP/api"
find "$APP/releases" -maxdepth 1 -regextype egrep -regex '.*/[0-9]{14}' -exec rm -rf {} + # релизы старой схемы
if [ -f /etc/systemd/system/wms-api.service ]; then
  rm -f /etc/systemd/system/wms-api.service && systemctl daemon-reload
fi

# ---------- 6. уборка (только объекты WMS) ----------
log "Уборка"
CUR="$(readlink -f "$APP/web")"
ls -1dt "$APP"/releases/web-* 2>/dev/null | grep -vxF "$CUR" | tail -n +3 | xargs -r rm -rf
docker images wms-api --format '{{.Tag}}' | grep -vxE 'latest|<none>' | tail -n +4 \
  | sed 's/^/wms-api:/' | xargs -r docker image rm >/dev/null 2>&1 || true
docker image prune -f --filter label=wms.component=api >/dev/null || true
docker buildx prune --builder "$BUILDER" -f --keep-storage 3gb >/dev/null 2>&1 || true
ls -1t "$APP"/backups/wms-*.dump 2>/dev/null | tail -n +11 | xargs -r rm -f

"${COMPOSE[@]}" ps --format 'table {{.Name}}\t{{.Status}}'
log "Готово: $SHA"

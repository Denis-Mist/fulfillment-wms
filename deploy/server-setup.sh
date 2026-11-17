#!/usr/bin/env bash
# Однократная (идемпотентная) настройка сервера. Запускать от root НА СЕРВЕРЕ из каталога с файлами deploy/.
#   SERVER_NAME=<IP или домен> ./server-setup.sh
# Не трогает чужие контейнеры/сети/тома, не перезапускает docker, не включает ufw.
set -euo pipefail
cd "$(dirname "$0")"
: "${SERVER_NAME:?укажите SERVER_NAME (IP или домен)}"
APP=/opt/wms

id wms >/dev/null 2>&1 || useradd --system --home-dir "$APP" --shell /usr/sbin/nologin wms
install -d -o root -g root -m 755 "$APP" "$APP/api" "$APP/web"
install -m 644 docker-compose.db.yml "$APP/docker-compose.db.yml"

if [ ! -f "$APP/.env" ]; then
  gen() { openssl rand -hex 32; }
  PG="$(gen)"
  umask 077
  cat > "$APP/.env" <<ENV
POSTGRES_PASSWORD=$PG
WMS_DB_PORT=${WMS_DB_PORT:-5432}
WMS_API_PORT=${WMS_API_PORT:-5080}
ConnectionStrings__Wms=Host=127.0.0.1;Port=${WMS_DB_PORT:-5432};Database=wms;Username=wms;Password=$PG
Jwt__SigningKey=$(gen)
Marketplace__ProtectionKey=$(gen)
Database__MigrateOnStartup=true
Database__Seed=true
ENV
  echo "Создан $APP/.env с новыми секретами"
fi
# EnvironmentFile читает systemd от root — достаточно 600
chmod 600 "$APP/.env"

# .env не source-им (строка подключения содержит ";"), берём только нужное
WMS_API_PORT="$(sed -n 's/^WMS_API_PORT=//p' "$APP/.env")"
(cd "$APP" && docker compose -f docker-compose.db.yml --env-file .env up -d)

install -m 644 systemd/wms-api.service /etc/systemd/system/wms-api.service
systemctl daemon-reload
systemctl enable wms-api.service >/dev/null

sed -e "s/__SERVER_NAME__/$SERVER_NAME/" -e "s/__API_PORT__/$WMS_API_PORT/" nginx/wms.conf > /etc/nginx/sites-available/wms
ln -sf /etc/nginx/sites-available/wms /etc/nginx/sites-enabled/wms
nginx -t && systemctl reload nginx
echo "Готово. Далее с машины сборки: deploy/deploy.sh"

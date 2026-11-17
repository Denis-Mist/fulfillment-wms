#!/usr/bin/env bash
# Однократная (идемпотентная) подготовка сервера. Запускать от root НА СЕРВЕРЕ:
#   curl -fsSL https://raw.githubusercontent.com/Denis-Mist/fulfillment-wms/main/deploy/server-setup.sh -o /tmp/wms-setup.sh
#   SERVER_NAME=wms.example.ru DEPLOY_PUBKEY="ssh-ed25519 AAAA... github-actions" bash /tmp/wms-setup.sh
# Затем первый деплой: wms-deploy
# Требования: Docker + compose plugin + buildx, git, nginx, openssl, curl, flock.
# Не трогает чужие контейнеры/сети/тома, не перезапускает docker, не включает ufw, существующий сайт nginx не перезаписывает.
set -euo pipefail
APP=/opt/wms
REPO="${WMS_REPO:-https://github.com/Denis-Mist/fulfillment-wms.git}"
BRANCH="${WMS_BRANCH:-main}"

install -d -m 755 "$APP" "$APP/releases" "$APP/state"
install -d -m 700 "$APP/backups"

# 1. клон репозитория (публичный — https; для приватного нужен read-only deploy key)
if [ ! -d "$APP/src/.git" ]; then
  git clone --branch "$BRANCH" "$REPO" "$APP/src"
fi

# 2. секреты
if [ ! -f "$APP/.env" ]; then
  gen() { openssl rand -hex 32; }
  umask 077
  cat > "$APP/.env" <<ENV
POSTGRES_PASSWORD=$(gen)
WMS_API_PORT=${WMS_API_PORT:-5080}
Jwt__SigningKey=$(gen)
Marketplace__ProtectionKey=$(gen)
Database__MigrateOnStartup=true
Database__Seed=true
ENV
  echo "Создан $APP/.env с новыми секретами"
fi
chmod 600 "$APP/.env"; chown root:root "$APP/.env"

# 3. точка входа деплоя
install -m 755 -o root -g root "$APP/src/deploy/wms-deploy" /usr/local/sbin/wms-deploy

# 4. пользователь для GitHub Actions: может только запустить wms-deploy (forced command + sudoers без аргументов)
id wms-deploy >/dev/null 2>&1 || useradd --create-home --shell /bin/bash wms-deploy
passwd -l wms-deploy >/dev/null
cat > /etc/sudoers.d/wms-deploy <<'SUDO'
# GitHub Actions -> только деплой WMS, без аргументов
wms-deploy ALL=(root) NOPASSWD: /usr/local/sbin/wms-deploy ""
Defaults:wms-deploy !requiretty
SUDO
chmod 440 /etc/sudoers.d/wms-deploy && visudo -cf /etc/sudoers.d/wms-deploy >/dev/null
install -d -m 700 -o wms-deploy -g wms-deploy ~wms-deploy/.ssh
if [ -n "${DEPLOY_PUBKEY:-}" ]; then
  printf 'restrict,command="sudo -n /usr/local/sbin/wms-deploy" %s\n' "$DEPLOY_PUBKEY" > ~wms-deploy/.ssh/authorized_keys
  chown wms-deploy:wms-deploy ~wms-deploy/.ssh/authorized_keys && chmod 600 ~wms-deploy/.ssh/authorized_keys
fi

# 5. сайт nginx (только если его ещё нет — certbot дописывает в него HTTPS)
if [ ! -f /etc/nginx/sites-available/wms ]; then
  : "${SERVER_NAME:?укажите SERVER_NAME (домен или IP)}"
  API_PORT="$(sed -n 's/^WMS_API_PORT=//p' "$APP/.env")"
  sed -e "s/__SERVER_NAME__/$SERVER_NAME/" -e "s/__API_PORT__/${API_PORT:-5080}/" \
    "$APP/src/deploy/nginx/wms.conf" > /etc/nginx/sites-available/wms
  ln -sf /etc/nginx/sites-available/wms /etc/nginx/sites-enabled/wms
  nginx -t && systemctl reload nginx
  echo "HTTPS: certbot --nginx -d $SERVER_NAME --redirect"
fi
echo "Готово. Первый деплой: wms-deploy"

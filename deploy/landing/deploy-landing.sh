#!/usr/bin/env bash
# Выкладка лендинга korobfulfillment.ru (nginx-сайт "mysite", root /var/www/mysite) из репозитория.
# Запуск на сервере от root, после того как автодеплой подтянул main в /opt/wms/src:
#   sudo bash /opt/wms/src/deploy/landing/deploy-landing.sh
# Что делает:
#   1. проверяет новый index.html (не пустой, есть </html>);
#   2. если файл на сервере уже такой же — ничего не делает;
#   3. бэкапит текущий /var/www/mysite/index.html в /opt/wms/backups/landing/index-<время>.html (хранит 20 последних);
#   4. атомарно подменяет файл (временный файл в той же папке + mv), владелец и права — как у старого файла.
# nginx не трогает: это статический файл, reload не нужен. Конфиг сайта mysite и root не меняются.
# Откат: см. команду, которую скрипт печатает в конце (или: deploy-landing.sh <файл-бэкапа>).
set -euo pipefail

SRC_FILE="${1:-$(cd "$(dirname "$0")" && pwd)/index.html}"
SITE_DIR="${LANDING_DIR:-/var/www/mysite}"
TARGET="$SITE_DIR/index.html"
BACKUP_DIR="${LANDING_BACKUP_DIR:-/opt/wms/backups/landing}"
KEEP=20

[ "$(id -u)" -eq 0 ] || { echo "запускать от root (sudo)" >&2; exit 1; }
[ -d "$SITE_DIR" ] || { echo "нет папки $SITE_DIR" >&2; exit 1; }
[ -s "$SRC_FILE" ] || { echo "нет или пустой файл $SRC_FILE" >&2; exit 1; }
grep -qi '</html>' "$SRC_FILE" || { echo "$SRC_FILE не похож на полный HTML (нет </html>)" >&2; exit 1; }

if [ -f "$TARGET" ] && cmp -s "$SRC_FILE" "$TARGET"; then
  echo "Лендинг уже актуален ($TARGET совпадает с $SRC_FILE), ничего не меняю."
  exit 0
fi

# владелец и права — как у текущего файла (если его нет — root:root 644)
if [ -f "$TARGET" ]; then
  OWNER="$(stat -c %u "$TARGET")"; GROUP="$(stat -c %g "$TARGET")"; MODE="$(stat -c %a "$TARGET")"
else
  OWNER=0; GROUP=0; MODE=644
fi

# сначала копируем новую версию во временный файл рядом с целью (так источником может быть и сам бэкап — откат)
TMP="$(mktemp "$SITE_DIR/.index.html.XXXXXX")"
trap 'rm -f "$TMP"' EXIT
cat "$SRC_FILE" > "$TMP"
chown "$OWNER:$GROUP" "$TMP"
chmod "$MODE" "$TMP"

BACKUP=""
if [ -f "$TARGET" ]; then
  install -d -m 700 "$BACKUP_DIR"
  BACKUP="$BACKUP_DIR/index-$(date +%Y%m%d-%H%M%S).html"
  n=1; while [ -e "$BACKUP" ]; do BACKUP="$BACKUP_DIR/index-$(date +%Y%m%d-%H%M%S)-$n.html"; n=$((n + 1)); done
  cp -p "$TARGET" "$BACKUP"
  echo "Бэкап: $BACKUP"
  # оставить только $KEEP последних бэкапов
  ls -1t "$BACKUP_DIR"/index-*.html 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f --
fi

mv -f "$TMP" "$TARGET"
trap - EXIT

echo "Выложено: $TARGET ($(stat -c '%U:%G %a, %s байт' "$TARGET"))"
if [ -n "$BACKUP" ]; then
  echo "Откат: sudo bash $(cd "$(dirname "$0")" && pwd)/$(basename "$0") $BACKUP"
fi

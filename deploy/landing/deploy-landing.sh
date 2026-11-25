#!/usr/bin/env bash
# Выкладка лендинга korobfulfillment.ru (nginx-сайт "mysite", root /var/www/mysite) из репозитория.
# Запуск на сервере от root, после того как автодеплой подтянул main в /opt/wms/src:
#   sudo bash /opt/wms/src/deploy/landing/deploy-landing.sh
#
# Выкладывает файлы из этой папки: index.html, robots.txt, sitemap.xml, llms.txt, llms-full.txt.
# Для каждого файла:
#   1. проверяет новую версию (не пустая; HTML с </html>, sitemap с </urlset>);
#   2. если на сервере уже такой же файл — пропускает;
#   3. бэкапит текущий файл в /opt/wms/backups/landing/<имя>-<время>.<расширение>
#      (например index-20261125-120000.html; хранит 20 последних для каждого файла);
#   4. атомарно подменяет файл (временный файл в той же папке + mv), владелец и права — как у старого
#      (новый файл: root:root 644).
# Сначала проверяются ВСЕ файлы, и только потом что-то меняется.
# nginx не трогает: это статика, reload не нужен. Конфиг сайта mysite и root не меняются.
#
# Откат одного файла: deploy-landing.sh <файл-бэкапа>
#   например: sudo bash deploy-landing.sh /opt/wms/backups/landing/index-20261125-120000.html
#   (имя цели берётся из имени бэкапа: index-<время>.html -> index.html). Команды отката печатаются в конце.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SITE_DIR="${LANDING_DIR:-/var/www/mysite}"
BACKUP_DIR="${LANDING_BACKUP_DIR:-/opt/wms/backups/landing}"
KEEP=20
FILES=(index.html robots.txt sitemap.xml llms.txt llms-full.txt)

[ "$(id -u)" -eq 0 ] || { echo "запускать от root (sudo)" >&2; exit 1; }
[ -d "$SITE_DIR" ] || { echo "нет папки $SITE_DIR" >&2; exit 1; }

# пары "источник|имя цели"
PAIRS=()
if [ $# -ge 1 ]; then
  SRC="$1"
  base="$(basename "$SRC")"
  # index-20261125-120000.html или index-20261125-120000-1.html -> index.html
  name="$(printf '%s' "$base" | sed -E 's/-[0-9]{8}-[0-9]{6}(-[0-9]+)?(\.[^.]+)$/\2/')"
  ok=0; for f in "${FILES[@]}"; do [ "$f" = "$name" ] && ok=1; done
  [ "$ok" -eq 1 ] || { echo "не понимаю, какой файл сайта заменить для $base (ожидаю одно из: ${FILES[*]})" >&2; exit 1; }
  PAIRS+=("$SRC|$name")
else
  for f in "${FILES[@]}"; do PAIRS+=("$SCRIPT_DIR/$f|$f"); done
fi

check() { # $1 источник, $2 имя цели
  [ -s "$1" ] || { echo "нет или пустой файл $1" >&2; return 1; }
  case "$2" in
    *.html) grep -qi '</html>' "$1" || { echo "$1 не похож на полный HTML (нет </html>)" >&2; return 1; } ;;
    sitemap.xml) grep -q '</urlset>' "$1" || { echo "$1 не похож на sitemap (нет </urlset>)" >&2; return 1; } ;;
  esac
}
for p in "${PAIRS[@]}"; do check "${p%%|*}" "${p##*|}"; done

TMP=""
trap '[ -n "$TMP" ] && rm -f "$TMP"' EXIT
ROLLBACK=()
CHANGED=0
for p in "${PAIRS[@]}"; do
  src="${p%%|*}"; name="${p##*|}"; target="$SITE_DIR/$name"
  if [ -f "$target" ] && cmp -s "$src" "$target"; then
    echo "= $name уже актуален"
    continue
  fi
  if [ -f "$target" ]; then
    OWNER="$(stat -c %u "$target")"; GROUP="$(stat -c %g "$target")"; MODE="$(stat -c %a "$target")"
  else
    OWNER=0; GROUP=0; MODE=644
  fi
  # сначала копия во временный файл рядом с целью (источником может быть и сам бэкап — откат)
  TMP="$(mktemp "$SITE_DIR/.$name.XXXXXX")"
  cat "$src" > "$TMP"
  chown "$OWNER:$GROUP" "$TMP"
  chmod "$MODE" "$TMP"

  if [ -f "$target" ]; then
    install -d -m 700 "$BACKUP_DIR"
    stem="${name%.*}"; ext="${name##*.}"
    backup="$BACKUP_DIR/$stem-$(date +%Y%m%d-%H%M%S).$ext"
    n=1; while [ -e "$backup" ]; do backup="$BACKUP_DIR/$stem-$(date +%Y%m%d-%H%M%S)-$n.$ext"; n=$((n + 1)); done
    cp -p "$target" "$backup"
    echo "  бэкап $name: $backup"
    ROLLBACK+=("sudo bash $SCRIPT_DIR/$(basename "$0") $backup")
    # оставить только $KEEP последних бэкапов этого файла (llms-*.txt не задевает бэкапы llms-full-*.txt)
    ls -1t "$BACKUP_DIR"/"$stem"-[0-9]*."$ext" 2>/dev/null | tail -n +$((KEEP + 1)) | xargs -r rm -f --
  else
    ROLLBACK+=("sudo rm -f $target   # файла раньше не было")
  fi

  mv -f "$TMP" "$target"; TMP=""
  CHANGED=$((CHANGED + 1))
  echo "+ выложено: $target ($(stat -c '%U:%G %a, %s байт' "$target"))"
done

if [ "$CHANGED" -eq 0 ]; then
  echo "Лендинг уже актуален, ничего не меняю."
  exit 0
fi
echo "Откат:"
for c in "${ROLLBACK[@]}"; do echo "  $c"; done

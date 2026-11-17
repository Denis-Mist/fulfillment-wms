#!/usr/bin/env bash
# Выкладка собранных артефактов (deploy/out) на сервер и перезапуск wms-api. Запускать на машине сборки.
#   WMS_SSH=root@<IP> ./deploy/deploy.sh            # полная выкладка: build + upload + restart
#   SKIP_BUILD=1 WMS_SSH=root@<IP> ./deploy/deploy.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
: "${WMS_SSH:?укажите WMS_SSH=user@host}"
SSH_OPTS=(${WMS_SSH_OPTS:-})
[ "${SKIP_BUILD:-0}" = 1 ] || "$ROOT/deploy/build.sh"

REL="$(date +%Y%m%d%H%M%S)"
tar -C "$ROOT/deploy/out" -czf "/tmp/wms-$REL.tgz" api web
scp "${SSH_OPTS[@]}" "/tmp/wms-$REL.tgz" "$WMS_SSH:/tmp/wms-$REL.tgz"
ssh "${SSH_OPTS[@]}" "$WMS_SSH" REL="$REL" bash -s <<'REMOTE'
set -euo pipefail
APP=/opt/wms
STAGE="$APP/releases/$REL"
mkdir -p "$STAGE" && tar -C "$STAGE" -xzf "/tmp/wms-$REL.tgz" && rm -f "/tmp/wms-$REL.tgz"
chown -R root:root "$STAGE" && chmod -R a+rX "$STAGE"
rsync -a --delete "$STAGE/api/" "$APP/api/"
rsync -a --delete "$STAGE/web/" "$APP/web/"
systemctl restart wms-api
. <(grep -E '^WMS_API_PORT=' "$APP/.env")
for i in $(seq 1 60); do
  curl -fsS "http://127.0.0.1:$WMS_API_PORT/health" >/dev/null 2>&1 && { echo "wms-api healthy"; break; }
  sleep 2
done
systemctl is-active wms-api
# держим 3 последних релиза
ls -1dt "$APP"/releases/* | tail -n +4 | xargs -r rm -rf
REMOTE
rm -f "/tmp/wms-$REL.tgz"

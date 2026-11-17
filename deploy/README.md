# Деплой Fulfillment WMS на Linux-сервер

Схема (рассчитана на сервер, где уже живут другие сервисы):

```
интернет ──:80──► nginx (системный, на хосте)
                   ├─ /api/*  → 127.0.0.1:${WMS_API_PORT}  wms-api (systemd, self-contained .NET 10, пользователь wms)
                   └─ /*      → /opt/wms/web (статика Vue, SPA fallback)
                                   wms-api → 127.0.0.1:${WMS_DB_PORT} PostgreSQL 17 (Docker, compose-проект "wms")
```

* Наружу открыт только веб-сервер (80, после домена ещё 443). API и Postgres слушают только `127.0.0.1`.
* Docker используется только для Postgres: проект `wms`, сеть `wms-net`, том `wms-pgdata`, контейнер `wms-postgres`.
  Скрипты не трогают другие контейнеры/сети/тома, не делают `docker system prune` и не перезапускают docker.
* Секреты (пароль Postgres, `Jwt__SigningKey`, `Marketplace__ProtectionKey`) генерирует `server-setup.sh`
  и хранит в `/opt/wms/.env` (root, `chmod 600`). В репозиторий они не попадают (см. `.env.example`).
* Миграции EF Core и демо-сиды применяются при старте API (`Database__Seed=true`; пароль демо-пользователей `demo`).
* Автозапуск: `wms-api.service` enabled (`Restart=always`), контейнер Postgres — `restart: unless-stopped`, docker — enabled.

## Текущая установка

* Адрес: **https://wms.korobfulfillment.ru** (DNS в Cloudflare, запись A в режиме *DNS only*: Let's Encrypt
  должен видеть сервер напрямую, без прокси Cloudflare).
* Сайт nginx `wms` (`server_name wms.korobfulfillment.ru`), сертификат Let's Encrypt выпущен `certbot --nginx`
  (HTTP → HTTPS 301), автопродление — системный `certbot.timer`.
* Другие сайты на этом nginx не затрагиваются; применение изменений — только `nginx -t && systemctl reload nginx`.

## Файлы

| Файл | Где используется |
|---|---|
| `build.sh` | машина сборки: собирает `out/api` (self-contained linux-x64) и `out/web` (Vite, `VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=/api`). Нужен только Docker |
| `deploy.sh` | машина сборки: build → scp на сервер → `/opt/wms/{api,web}` → `systemctl restart wms-api` → проверка `/health` |
| `server-setup.sh` | сервер, однократно (идемпотентно): пользователь `wms`, каталоги, `.env` с секретами, Postgres, systemd-юнит, сайт nginx |
| `docker-compose.db.yml` | сервер: PostgreSQL (`/opt/wms/docker-compose.db.yml`) |
| `systemd/wms-api.service` | сервер: `/etc/systemd/system/wms-api.service` |
| `nginx/wms.conf` | сервер: `/etc/nginx/sites-available/wms` (плейсхолдеры `__SERVER_NAME__`, `__API_PORT__`) |
| `.env.example` | образец `/opt/wms/.env` без секретов |

## Первый деплой

Требования к серверу: Ubuntu/Debian, Docker + compose plugin, nginx, rsync, openssl, curl; свободные `127.0.0.1:5080` и `127.0.0.1:5432`
(иначе задайте `WMS_API_PORT` / `WMS_DB_PORT` при первом запуске `server-setup.sh`). .NET на сервере не нужен.

```bash
# 1. На машине сборки: собрать артефакты
./deploy/build.sh

# 2. Скопировать deploy/ на сервер и настроить его (однократно)
tar -C deploy --exclude out -czf - . | ssh root@<SERVER_IP> 'mkdir -p /root/wms-deploy && tar -C /root/wms-deploy -xzf -'
ssh root@<SERVER_IP> 'SERVER_NAME=<SERVER_IP> /root/wms-deploy/server-setup.sh'

# 3. Выложить приложение
SKIP_BUILD=1 WMS_SSH=root@<SERVER_IP> ./deploy/deploy.sh
```

`server_name` в сайте nginx = IP (или домен), поэтому сайт WMS уживается с другими сайтами на том же nginx
(name-based virtual hosts). Если WMS должен отвечать на любой Host, замените `server_name` на `_` и сделайте сайт
`default_server`, убрав флаг у текущего default-сайта.

## Обновление

```bash
git pull
WMS_SSH=root@<SERVER_IP> ./deploy/deploy.sh        # пересоберёт фронт и бэк, выложит, перезапустит wms-api
```

Миграции БД применяются автоматически при старте. Последние 3 релиза лежат в `/opt/wms/releases/` —
откат: `rsync -a --delete /opt/wms/releases/<REL>/api/ /opt/wms/api/ && systemctl restart wms-api` (то же для `web`).

## Эксплуатация

```bash
systemctl status wms-api            journalctl -u wms-api -f
docker compose -p wms ps            docker logs wms-postgres
# бэкап БД
docker exec wms-postgres pg_dump -U wms wms | gzip > wms-$(date +%F).sql.gz
```

## Домен и HTTPS

1. Купить домен, создать DNS A-запись `wms.example.ru → <SERVER_IP>`.
2. Указать домен в nginx: в `/etc/nginx/sites-available/wms` `server_name wms.example.ru;` (или заново
   `SERVER_NAME=wms.example.ru ./server-setup.sh`), затем `nginx -t && systemctl reload nginx`.
3. Выпустить сертификат Let's Encrypt (только для этого домена):
   `certbot --nginx -d wms.example.ru --redirect --non-interactive --agree-tos`
   (certbot сам добавит `listen 443 ssl` и редирект 80→443, продление — systemd-таймер certbot).
   Проверка продления: `certbot renew --dry-run --cert-name wms.example.ru` (без TTY certbot сначала ждёт
   случайную паузу до ~8 минут — это нормально).
   Если DNS в Cloudflare — запись должна быть *DNS only* (серое облако).
4. Открыть 443/tcp в firewall, если он включён (`ufw allow 443/tcp`).

## Firewall

Скрипты firewall не меняют. Если `ufw` активен — `ufw allow 80/tcp` (и 443 после HTTPS). Postgres и API наружу
не публикуются (bind `127.0.0.1`), поэтому дополнительных правил для них не нужно.

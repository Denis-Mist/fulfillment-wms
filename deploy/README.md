# Деплой Fulfillment WMS

Сервер сам тянет код из GitHub и собирает всё у себя; после merge в `main` деплой запускается автоматически
из GitHub Actions.

```
интернет ──:80/:443──► nginx (системный, на хосте; HTTPS — certbot)
                        ├─ /api/*  → 127.0.0.1:5080 ─► контейнер wms-api  (образ wms-api, собран на сервере из backend/Dockerfile)
                        └─ /*      → /opt/wms/web  (симлинк на /opt/wms/releases/web-<sha>, статика Vue, SPA fallback)
                                                     wms-api ─► wms-postgres (PostgreSQL 17, только сеть wms-net)

GitHub: merge в main ─► workflow deploy: backend-tests + frontend-tests ─► deploy (ssh wms-deploy@сервер)
         └─► на сервере: /usr/local/sbin/wms-deploy → git reset origin/main → deploy/server-deploy.sh
```

* Docker: один compose-проект `wms` (`deploy/docker-compose.yml`) — сервисы `api` и `postgres`,
  контейнеры `wms-api`, `wms-postgres`, сеть `wms-net`, том `wms-pgdata`. `restart: unless-stopped`, healthcheck'и,
  `api` стартует после `postgres: service_healthy`, лимиты памяти по 512 МБ.
* Наружу открыт только nginx. API публикуется только на `127.0.0.1:5080`, Postgres не публикуется вообще.
* Фронт собирается в одноразовом контейнере `node:22-alpine` (`VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=/api`),
  node на хост не ставится. Переключение — атомарная замена симлинка `/opt/wms/web`.
* Секреты — только в `/opt/wms/.env` на сервере (root, 600), см. `.env.example`. В репозитории их нет.
* Миграции EF Core и демо-сиды применяются при старте API (пароль демо-пользователей `demo`).
* Сервер общий с другими сервисами: скрипты трогают только объекты WMS, не делают `docker system prune`,
  не перезапускают docker, nginx — только `reload`.

## Файлы

| Файл | Назначение |
|---|---|
| `../backend/Dockerfile` | multi-stage образ API (sdk → aspnet, пользователь `app`). Сборка экономная: `-m:1`, без shared compilation |
| `docker-compose.yml` | compose-проект `wms`: `postgres` + `api` |
| `server-deploy.sh` | деплой на сервере (см. ниже) |
| `wms-deploy` | точка входа на сервере (`/usr/local/sbin/wms-deploy`): flock → `git fetch` + `reset --hard origin/main` → `server-deploy.sh` |
| `server-setup.sh` | однократная подготовка сервера: клон, `.env`, `wms-deploy`, пользователь `wms-deploy`, sudoers, сайт nginx |
| `nginx/wms.conf` | шаблон сайта nginx (`/etc/nginx/sites-available/wms`); HTTPS-блоки дописывает certbot |
| `.env.example` | образец `/opt/wms/.env` без секретов |
| `../.github/workflows/deploy.yml` | автодеплой: push в `main` (merge PR) или вручную (Run workflow) |
| `landing/index.html` | лендинг https://korobfulfillment.ru (nginx-сайт `mysite`, `/var/www/mysite`). Автодеплоем **не** выкладывается |
| `landing/deploy-landing.sh` | ручная выкладка лендинга и robots/sitemap/llms.txt с бэкапом (см. «Лендинг») |

### Что делает `server-deploy.sh`

1. Собирает образ `wms-api:<sha>` через `docker buildx` в отдельном builder'е `wms-builder` (driver docker-container)
   с жёстким лимитом памяти (`WMS_BUILD_MEM`, по умолчанию 1100m) — сборка не может вытеснить соседние сервисы
   (сервер: 2 ГБ RAM без swap). NuGet-кеш — cache mount, повторные сборки быстрее.
2. Собирает фронт в `node:22-alpine` (тот же лимит памяти, кеш npm в томе `wms-npm-cache`) в `/opt/wms/releases/web-<sha>`.
3. Делает `pg_dump` в `/opt/wms/backups/wms-<время>-<sha>.dump` (хранятся 10 последних).
4. `docker compose up -d --wait`: пересоздаёт `wms-api` на новом образе и ждёт healthy.
   Если API не поднялся — откатывает `wms-api:latest` на предыдущий образ, фронт **не** переключает, выходит с ошибкой.
5. Атомарно переключает `/opt/wms/web` на новый фронт.
6. Уборка только своего: 3 релиза фронта, 3 образа `wms-api`, кеш `wms-builder` до 3 ГБ, 10 дампов; builder останавливается (не держит RAM).

Пока идёт сборка (шаги 1–2, ~5–10 минут), работает старая версия; простой — только пересоздание контейнера API (~20–40 с).

## Автодеплой (GitHub Actions)

`deploy.yml` запускается на push в `main` (т.е. после merge PR) и вручную (`workflow_dispatch`).
Сначала прогоняются `backend-tests` и `frontend-tests` (они же идут на каждом PR), затем job `deploy`
(`concurrency: wms-deploy-production`, деплои встают в очередь, не отменяются) заходит по SSH и проверяет сайт.

Секреты репозитория (Settings → Secrets and variables → Actions):

| Секрет | Значение |
|---|---|
| `DEPLOY_HOST` | IP/имя сервера |
| `DEPLOY_USER` | `wms-deploy` |
| `DEPLOY_SSH_KEY` | приватный ключ ed25519 (отдельный, только для Actions) |
| `DEPLOY_KNOWN_HOSTS` | вывод `ssh-keyscan -t ed25519 <host>` (проверка ключа сервера) |
| `DEPLOY_PORT` | необязательно, по умолчанию 22 |

Ключ на сервере ограничен: `~wms-deploy/.ssh/authorized_keys` →
`restrict,command="sudo -n /usr/local/sbin/wms-deploy" ssh-ed25519 ...`, а sudoers разрешает пользователю
`wms-deploy` только `/usr/local/sbin/wms-deploy` без аргументов. Шелла, проброса портов и доступа к docker у ключа нет.

## Первичная установка на новый сервер

Требования: Ubuntu/Debian, Docker + compose plugin + buildx, git, nginx, openssl, curl; свободный `127.0.0.1:5080`.

```bash
# на сервере, от root
curl -fsSL https://raw.githubusercontent.com/Denis-Mist/fulfillment-wms/main/deploy/server-setup.sh -o /tmp/wms-setup.sh
SERVER_NAME=wms.example.ru DEPLOY_PUBKEY="ssh-ed25519 AAAA... github-actions" bash /tmp/wms-setup.sh
wms-deploy                                          # первый деплой
certbot --nginx -d wms.example.ru --redirect        # HTTPS (DNS A-запись должна смотреть на сервер; в Cloudflare — DNS only)
```

Ключ для Actions: `ssh-keygen -t ed25519 -N '' -C github-actions-wms-deploy -f wms_actions` — `wms_actions.pub`
в `DEPLOY_PUBKEY`, `wms_actions` в секрет `DEPLOY_SSH_KEY`.

## Эксплуатация

```bash
wms-deploy                                                    # деплой текущего origin/main вручную (root)
# или GitHub → Actions → deploy → Run workflow

cd /opt/wms/src/deploy
docker compose -p wms --env-file /opt/wms/.env ps             # статус
docker compose -p wms --env-file /opt/wms/.env logs -f api    # логи API
docker compose -p wms --env-file /opt/wms/.env restart api
docker exec -it wms-postgres psql -U wms wms                  # консоль БД
cat /opt/wms/state/deployed-sha                               # что сейчас задеплоено
```

**Откат API** на предыдущий образ: `docker images wms-api` → `docker tag wms-api:<старый sha> wms-api:latest &&
docker compose -p wms --env-file /opt/wms/.env up -d api`.
**Откат фронта**: `ln -sfn /opt/wms/releases/web-<sha> /opt/wms/web.new && mv -T /opt/wms/web.new /opt/wms/web`.
Надёжнее — revert-коммит в `main`: автодеплой сделает всё сам.

**Бэкап/восстановление БД**:
```bash
docker exec wms-postgres pg_dump -U wms -d wms -Fc > /opt/wms/backups/manual.dump
docker exec -i wms-postgres pg_restore -U wms -d wms --clean --if-exists < /opt/wms/backups/manual.dump
```

Автозапуск после перезагрузки: docker enabled, оба контейнера `restart: unless-stopped`, nginx enabled.

## Лендинг korobfulfillment.ru

Исходники — `deploy/landing/`: `index.html` (один самодостаточный файл: стили, скрипты и JSON-LD внутри, шрифты с Google Fonts),
`robots.txt`, `sitemap.xml`, `llms.txt`, `llms-full.txt` (подробности и правила правки — `deploy/landing/README.md`).
Автодеплой WMS их **не** выкладывает: после merge они лишь появляются в `/opt/wms/src`. Выложить вручную, на сервере от root:

```bash
sudo bash /opt/wms/src/deploy/landing/deploy-landing.sh
```

Скрипт сначала проверяет все файлы, затем для каждого изменённого бэкапит текущую версию из `/var/www/mysite/`
в `/opt/wms/backups/landing/<имя>-<время>.<расширение>` (20 последних на файл) и атомарно подменяет его (temp + `mv`),
сохраняя владельца и права. nginx не трогается (reload не нужен). В конфиге `mysite` нужен `charset utf-8;` (для `.txt`).
Откат — тот же скрипт с путём к бэкапу (команды он печатает): `sudo bash /opt/wms/src/deploy/landing/deploy-landing.sh /opt/wms/backups/landing/index-<время>.html`.

## Текущая установка

* **https://wms.korobfulfillment.ru** — DNS в Cloudflare, A-запись в режиме *DNS only* (Let's Encrypt должен видеть
  сервер напрямую). Сертификат `certbot --nginx`, автопродление — `certbot.timer`.
* Сервер общий с другим сервисом (Telegram-бот в Docker): WMS живёт только в `/opt/wms`, проекте compose `wms`
  и сайте nginx `wms`; чужие контейнеры, тома и сайты не трогаются.

## История

До ноября 2026 API работал как systemd-сервис `wms-api` (self-contained сборка на внешней машине, `scp`).
`server-deploy.sh` при первом запуске сам мигрирует: отключает и удаляет юнит, переносит фронт-каталог в симлинк,
убирает `/opt/wms/api` и старый `docker-compose.db.yml` (сохраняется в `/opt/wms/backups`). Том `wms-pgdata` и секреты
в `/opt/wms/.env` при этом не меняются.

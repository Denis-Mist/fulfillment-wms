# Лендинг korobfulfillment.ru

Статический сайт (nginx-сайт `mysite`, root `/var/www/mysite`). Файлы:

| Файл | Зачем |
|---|---|
| `index.html` | сам лендинг; внутри JSON-LD (`@graph`: Organization/LocalBusiness, Service + OfferCatalog с ценами, WebSite, WebPage, FAQPage) |
| `robots.txt` | доступ для поисковых и ИИ-краулеров открыт явно + ссылка на sitemap |
| `sitemap.xml` | карта сайта |
| `llms.txt`, `llms-full.txt` | краткое и полное markdown-описание компании, цен и контактов для ИИ-ассистентов |

Выкладка (на сервере, после того как автодеплой подтянул main в `/opt/wms/src`):

```bash
sudo bash /opt/wms/src/deploy/landing/deploy-landing.sh
```

Скрипт бэкапит каждый изменённый файл в `/opt/wms/backups/landing/` и печатает команды отката.

## Правила правки

- Только реальные факты: цены, адрес, телефоны, часы работы берутся из прайса и от владельца. Без выдуманных отзывов и рейтингов (`AggregateRating`/`Review` — только с реальными данными).
- Цены меняются в нескольких местах сразу: JS-прайс и калькулятор в `index.html`, статическая таблица «Весь прайс КОРОБ одной таблицей», блок «Кратко о КОРОБ», FAQ, JSON-LD, `llms.txt`, `llms-full.txt`. FAQ в JSON-LD должен совпадать с видимым FAQ.
- При изменении страницы обновить `<lastmod>` в `sitemap.xml` и `dateModified` в JSON-LD.

## nginx

Для корректной кириллицы в `.txt` в server-блоке `mysite` (443) стоит `charset utf-8;`
(отдаётся `text/plain; charset=utf-8`). Других особых правил не нужно: `try_files $uri $uri/ =404`.

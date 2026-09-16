# Still Awake? — Backend Lab v4

Локальный dev-стенд для Still Awake?: Go backend + SQLite + responsive frontend + LLM provider adapter.

## Что есть в v4

Интерфейс разделён на 4 нижних меню:

1. **AI** — настройки LM Studio/OpenAI-compatible provider, список моделей и live status backend / LLM server / выбранной модели.
2. **Chat** — `installation_id` / `session_id`, один чат, game state, отправка запросов и два debug-view: client body и фактический `messages[]`, собранный backend с историей из SQLite.
3. **Analytics** — отдельный read-only `ANALYTICS_TOKEN`: запросы, ошибки, users, sessions, токены, latency, tok/s, TTFT, статистика по дням, моделям и installations.
4. **Admin** — CRUD, поиск по installation/user ID и session ID, история конкретного пользователя, история session, связанные пары user request → model response, фактический prompt, schema viewer и SQL console.

## Структура

```text
cmd/server/                  запуск приложения
internal/httpapi/            HTTP API / backend handlers
internal/llm/                adapter к LLM provider + status check
internal/db/                 SQLite store + schema + analytics + admin SQL
internal/frontend/           frontend package
internal/frontend/web/       HTML / CSS / JS
examples/                    пример Unity-клиента
data/still-awake.db          локальная SQLite БД
```

Frontend логически отделён, но для dev встраивается в один Go binary. SQLite работает в том же backend-процессе.

## Запуск

```powershell
Copy-Item .env.example .env
# поменяй ADMIN_TOKEN и ANALYTICS_TOKEN
go mod tidy
go run ./cmd/server
```

Открыть:

```text
http://127.0.0.1:8080
```

## LM Studio

В меню **AI**:

```text
Provider mode: OpenAI-compatible
Base URL:     http://127.0.0.1:1234
Chat path:    /v1/chat/completions
API token:    sk-lm-...       # только если Require Authentication включён
Model ID:     получить кнопкой «Получить модели»
```

Кнопка **Проверить всё** проверяет:

- `/healthz` backend;
- доступность LM Studio;
- наличие выбранной модели;
- через LM Studio native `/api/v1/models` — загружена ли модель сейчас, effective context length и max context length (если endpoint поддерживается).

## Backend history

Сейчас реализован только `backend_history`.

Frontend отправляет:

```json
{
  "request_mode": "backend_history",
  "history_limit": 30,
  "user_message": "..."
}
```

Сами прошлые 30 сообщений в этом client body **не дублируются**. Backend получает их из SQLite по `session_id`, после чего формирует:

```text
system prompt
+ последние N messages из SQLite
+ GAME_STATE / EXTRA_CONTEXT
+ текущее user message
```

В Chat → **Request / prompt debug** теперь можно увидеть этот итоговый `messages[]` через `POST /v1/sessions/{id}/prompt-preview`. После реального запроса resolved prompt также возвращается в `debug` ответа.

## Linked LLM requests

`llm_requests` теперь хранит не только метрики, но и связь запроса с диалогом:

```text
installation_id
session_id
user_message_id
assistant_message_id
user_message
assistant_message
request_messages_json   # фактический messages[] для модели
history_count
provider / model / endpoint
prompt/completion/total tokens
latency / tok/s / TTFT
status / error
created_at
```

Существующая SQLite БД мигрируется при старте: backend добавляет отсутствующие v4-колонки через `ALTER TABLE`.

## Analytics token

В `.env`:

```env
ANALYTICS_TOKEN=change-this-analytics-token
```

Analytics endpoint принимает только:

```text
X-Analytics-Token: ...
```

Он не даёт SQL/CRUD-доступ. `ADMIN_TOKEN` остаётся отдельным ключом для админки.

Сейчас аналитика показывает:

- requests / success / errors;
- active installations и sessions;
- input / output / total tokens;
- avg latency, tok/s, TTFT;
- requests/tokens/errors по дням;
- статистику по моделям;
- top installations/users с количеством sessions и запросов.

## Admin search / inspectors

Поиск принимает installation/user ID или session ID. Кроме общего списка:

- `History` у installation открывает все sessions и LLM requests этого пользователя;
- `Inspect` у session показывает messages/events и LLM requests конкретной session;
- каждый LLM request раскрывается и показывает user message, model response и фактический `messages[]`.

## API

### Game / debug

```text
GET    /healthz
POST   /v1/installations
POST   /v1/sessions
GET    /v1/sessions/{id}
POST   /v1/sessions/{id}/events
GET    /v1/sessions/{id}/messages
POST   /v1/sessions/{id}/chat
POST   /v1/sessions/{id}/prompt-preview
POST   /v1/debug/provider/models
POST   /v1/debug/provider/status
```

### Analytics (`X-Analytics-Token`)

```text
GET /v1/analytics/summary?days=7
```

`days=0` означает всё время.

### Admin (`X-Admin-Token`)

```text
GET    /v1/admin/overview
GET    /v1/admin/installations?q=...
PATCH  /v1/admin/installations/{id}
DELETE /v1/admin/installations/{id}
GET    /v1/admin/sessions?q=...&installation_id=...
PATCH  /v1/admin/sessions/{id}
DELETE /v1/admin/sessions/{id}
GET    /v1/admin/sessions/{id}/messages
GET    /v1/admin/sessions/{id}/events
GET    /v1/admin/sessions/{id}/llm-requests
PATCH  /v1/admin/messages/{id}
DELETE /v1/admin/messages/{id}
PATCH  /v1/admin/events/{id}
DELETE /v1/admin/events/{id}
GET    /v1/admin/llm-requests?q=...&installation_id=...&session_id=...
DELETE /v1/admin/llm-requests/{id}
GET    /v1/admin/db/schema
POST   /v1/admin/db/query
```

## Security notes

- AI provider API key не сохраняется frontend-ом и теперь по умолчанию **не показывается в Raw request preview**.
- `ADMIN_TOKEN`, `ANALYTICS_TOKEN` и provider keys не коммить в Git.
- SQL console и client provider overrides — dev-only.
- Для production отключить SQL writes/provider overrides, добавить нормальную user auth, rate limits и HTTPS.

## Проверка сборки

В sandbox нет сетевого доступа для скачивания `modernc.org/sqlite`, поэтому Go-пакеты compile-checked с временным stub-модулем SQLite, а frontend проверен через `node --check`. На обычном ПК `go mod tidy` скачает настоящий драйвер.

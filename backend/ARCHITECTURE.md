# Still Awake? Backend Lab — architecture v4

```text
┌──────────────────────────────────────────┐
│ Responsive Web Frontend                  │
│ AI / Chat / Analytics / Admin            │
└───────────────────┬──────────────────────┘
                    │ HTTP JSON
                    ▼
┌──────────────────────────────────────────┐
│ Go Backend                               │
│ game API / analytics API / admin API     │
│ history resolver / provider proxy        │
└──────────┬───────────────────┬───────────┘
           │                   │
           ▼                   ▼
┌──────────────────┐   ┌───────────────────┐
│ SQLite           │   │ LLM adapter       │
│ installations    │   │ status + inference│
│ sessions         │   └─────────┬─────────┘
│ messages         │             │
│ events           │             ▼
│ llm_requests     │     ┌─────────────────┐
└──────────────────┘     │ LM Studio       │
                         │ Qwen / other LLM│
                         └─────────────────┘
```

Backend + SQLite = один deployment. LM Studio = отдельный inference service. Frontend assets раздаются backend-ом, но код лежит отдельно.

## Identity / history

```text
installation_id (условный user)
    └── session_id (одно прохождение / один чат)
          ├── messages
          ├── events
          └── llm_requests
```

Для `backend_history` backend по `session_id` читает последние N messages из SQLite. Client не обязан пересылать историю сам.

```text
Client body
  history_limit=30
  user_message
       │
       ▼
Backend
  SELECT last 30 messages
       │
       ├─ system prompt
       ├─ 30 previous messages
       ├─ GAME_STATE / EXTRA_CONTEXT
       └─ current user message
       │
       ▼
/v1/chat/completions
```

`prompt-preview` использует тот же resolver, поэтому показывает именно то, что затем уйдёт модели.

## Request audit

Одна строка `llm_requests` связывает вызов модели с парой сообщений:

```text
llm_request
├─ installation_id
├─ session_id
├─ user_message_id ──────> messages(user)
├─ assistant_message_id ─> messages(assistant)
├─ request_messages_json
└─ usage/performance/status
```

Это позволяет в Admin раскрыть один inference целиком, а также отфильтровать историю по user или session.

## Auth scopes

```text
Game/debug endpoints       dev/public rules проекта
Analytics                  X-Analytics-Token
Admin + SQL                X-Admin-Token
LM Studio                  Authorization: Bearer <provider token>
```

Analytics token специально не даёт права на SQL или CRUD.

## Service status

AI tab проверяет:

```text
Backend /healthz
LM Studio /api/v1/models (preferred)
          /v1/models     (fallback)
```

Native LM Studio v1 позволяет увидеть loaded instance и effective context length. OpenAI-compatible fallback подтверждает только доступность/наличие модели.

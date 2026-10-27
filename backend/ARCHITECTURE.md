# Still Awake? backend — архитектура

```text
┌──────────────────┐   ┌──────────────────────────┐
│ Unity-клиент     │   │ Панель (Preact + htm)    │
│ Bearer-токен     │   │ cookie-сессия + роли     │
└────────┬─────────┘   └────────────┬─────────────┘
         │ /v1/game/*               │ /v1/auth, /v1/admin, /v1/llm, ...
         ▼                          ▼
┌───────────────────────────────────────────────────┐
│ Go backend (один процесс, в Docker — один образ)  │
│ auth + RBAC · игровое API · сборка промпта ·      │
│ журнал запросов к модели · аналитика · SQL        │
└──────────┬──────────────────────────┬─────────────┘
           ▼                          ▼
┌───────────────────┐        ┌─────────────────────┐
│ SQLite (./data)   │        │ LLM: LM Studio /    │
│ users, roles,     │        │ OpenAI-совместимый  │
│ sessions, events, │        └─────────────────────┘
│ messages, ...     │
└───────────────────┘
```

## Доступ

```text
user ── user_roles ── role ── role_permissions ── permission
  │                    └ priority (super_admin 100 … player 10)
  ├── auth_sessions (web — cookie sa_session, game — Bearer; в БД только SHA-256 токена)
  ├── username_history
  └── login_history
```

Каждый маршрут обёрнут в `need(права...)`. Для запросов с cookie обязателен заголовок `X-Requested-With`
(защита от CSRF). Иерархия: управлять пользователем и назначать роль можно только ниже своего приоритета.

## Ночь

```text
player (kind=player, роль player)
  └── installation
        └── session (одна ночь)
              ├── events      night_start, stalker, hide, reply, night_end, ...
              ├── messages    user / assistant
              └── llm_requests (фактический messages[], токены, задержка, ошибка)
```

## Ход переписки

```text
игра: {message | event, state}
        │
        ▼
gameChat ── владелец сессии? лимит 20/мин? ровно одно из message / event?
        │
        ▼
resolveChatRequest
   system   персона из панели + guardPrompt          ← не меняется между ходами (кеш LM Studio)
   history  последние 20 сообщений; user — JSON {"от":"она","текст":...}
   user     реплика игрока (sanitizePlayerText → JSON)
   system   renderSituation(state) + ЗАДАЧА(event)   ← только белые списки значений
        │
        ▼
llm.Chat (max_tokens 160, таймаут 100 с) → cleanReply → messages + llm_requests → {reply}
```

Если модель не ответила вовремя, игра показывает скриптовую реплику: события ночи от сервера не зависят.
Отладочный чат и `prompt-preview` используют тот же `resolveChatRequest`, поэтому показывают ровно то, что уйдёт
модели.

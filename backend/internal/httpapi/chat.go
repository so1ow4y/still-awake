package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"strings"
	"sync"
	"time"

	"still-awake-backend/internal/db"
	"still-awake-backend/internal/llm"
)

const (
	// gameHistoryLimit — сколько прошлых сообщений ночи видит модель в игре.
	gameHistoryLimit = 20
	// gameMaxTokens — ответ сталкера короткий; лимит ещё и ускоряет локальную модель.
	gameMaxTokens = 160
	// gameChatTimeout — с запасом на загрузку модели в LM Studio при первом запросе и на модель без GPU.
	gameChatTimeout = 100 * time.Second
	// gameChatPerMinute — больше сообщений в минуту от одного игрока не бывает при честной игре.
	gameChatPerMinute = 20
)

type chatInput struct {
	UserMessage  string          `json:"user_message"`
	Event        string          `json:"event,omitempty"`
	GameState    json.RawMessage `json:"game_state"`
	Context      json.RawMessage `json:"context"`
	SystemPrompt string          `json:"system_prompt"`
	ClientSeq    int64           `json:"client_seq"`
	RawMessages  []llm.Message   `json:"raw_messages"`
	Provider     *llm.Config     `json:"provider,omitempty"`
	RequestMode  string          `json:"request_mode,omitempty"`
	HistoryLimit int             `json:"history_limit,omitempty"`
}

type resolvedChat struct {
	Session      db.Session
	Provider     llm.Config
	Messages     []llm.Message
	HistoryCount int
	HistoryLimit int
	RequestMode  string
	PromptSource string
}

func normalizeChatInput(in *chatInput) error {
	if in.RequestMode == "" {
		in.RequestMode = "backend_history"
	}
	if in.RequestMode != "backend_history" {
		return fmt.Errorf("request_mode %q is reserved for a future implementation; use backend_history", in.RequestMode)
	}
	if in.HistoryLimit <= 0 {
		in.HistoryLimit = 30
	}
	if in.HistoryLimit > 100 {
		in.HistoryLimit = 100
	}
	if in.Event != "" {
		if _, ok := gameEvents[in.Event]; !ok {
			return fail(http.StatusBadRequest, "EVENT_UNKNOWN", fmt.Sprintf("Неизвестное событие %q", in.Event))
		}
	}
	return nil
}

// chatTask — строка «ЗАДАЧА» системного сообщения: повод написать первым или ответ на реплику.
func chatTask(in chatInput) string {
	if in.Event != "" {
		return gameEvents[in.Event] + " Напиши одно сообщение."
	}
	if in.UserMessage != "" {
		return "Ответь на её последнее сообщение."
	}
	return ""
}

var errOverridesDisabled = errors.New("client provider overrides are disabled")

// resolveChatRequest собирает messages[] для модели (схема — в prompt.go): персона из панели, правила сервера,
// ситуация, история из SQLite и реплика игрока. Подмены провайдера, промпта и истории — только в отладке.
func (s *Server) resolveChatRequest(ctx context.Context, sessionID string, in chatInput, allowOverrides bool) (resolvedChat, error) {
	var out resolvedChat
	if err := normalizeChatInput(&in); err != nil {
		return out, err
	}
	session, err := s.store.GetSession(ctx, sessionID)
	if err != nil {
		return out, errors.New("session not found")
	}

	cfg, _, err := s.effectiveProvider(ctx)
	if err != nil {
		return out, err
	}
	if in.Provider != nil {
		if !allowOverrides {
			return out, errOverridesDisabled
		}
		override := *in.Provider
		if override.APIKey == "" {
			override.APIKey = s.cfg.DefaultProvider.APIKey
		}
		if err := s.validateProvider(override); err != nil {
			return out, err
		}
		cfg = override
	}

	prompt, err := s.activePrompt(ctx)
	if err != nil {
		return out, err
	}
	persona, source := prompt.Text, "default"
	if prompt.Active != nil {
		source = fmt.Sprintf("version #%d", prompt.Active.ID)
	}
	if in.SystemPrompt != "" && allowOverrides {
		persona, source = in.SystemPrompt, "draft"
	}

	situation := renderSituation(parseGameState(in.GameState))
	// Дополнительный контекст бывает только в отладочном чате (право llm.debug), в игре его нет.
	if extra := compactJSON(in.Context); allowOverrides && extra != "{}" && extra != "null" {
		situation = strings.TrimSpace(situation + "\nДОП. КОНТЕКСТ ИЗ ЛАБОРАТОРИИ: " + extra)
	}

	messages := []llm.Message{{Role: "system", Content: buildSystemMessage(persona)}}
	historyCount := 0
	if len(in.RawMessages) > 0 && allowOverrides {
		messages = append(messages, in.RawMessages...)
		historyCount = len(in.RawMessages)
	} else {
		history, err := s.store.ListMessages(ctx, sessionID, in.HistoryLimit)
		if err != nil {
			return out, err
		}
		historyCount = len(history)
		for _, m := range history {
			switch m.Role {
			case "user":
				messages = append(messages, wrapPlayerMessage(m.Content))
			case "assistant":
				messages = append(messages, llm.Message{Role: "assistant", Content: m.Content})
			}
		}
	}
	if in.UserMessage != "" {
		messages = append(messages, wrapPlayerMessage(in.UserMessage))
	}
	if turn := buildTurnMessage(situation, chatTask(in)); turn != "" {
		messages = append(messages, llm.Message{Role: "system", Content: turn})
	}
	out = resolvedChat{
		Session: session, Provider: cfg, Messages: messages,
		HistoryCount: historyCount, HistoryLimit: in.HistoryLimit, RequestMode: in.RequestMode, PromptSource: source,
	}
	return out, nil
}

func publicProviderConfig(cfg llm.Config) map[string]any {
	return map[string]any{
		"mode": cfg.Mode, "base_url": cfg.BaseURL, "path": cfg.Path,
		"model": cfg.Model, "temperature": cfg.Temperature, "api_key_set": cfg.APIKey != "",
	}
}

func chatError(err error) error {
	var e *apiError
	switch {
	case errors.As(err, &e):
		return e
	case strings.Contains(err.Error(), "reserved for a future"):
		return fail(http.StatusNotImplemented, "NOT_IMPLEMENTED", err.Error())
	case errors.Is(err, errOverridesDisabled):
		return fail(http.StatusForbidden, "OVERRIDES_DISABLED", err.Error())
	case err.Error() == "session not found":
		return fail(http.StatusNotFound, "NOT_FOUND", "Сессия не найдена")
	}
	return fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error())
}

func (s *Server) promptPreview(w http.ResponseWriter, r *http.Request, allowOverrides bool) {
	var in chatInput
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	resolved, err := s.resolveChatRequest(r.Context(), r.PathValue("id"), in, allowOverrides)
	if err != nil {
		writeAPIError(w, chatError(err))
		return
	}
	writeJSON(w, 200, map[string]any{
		"request_mode":  resolved.RequestMode,
		"history_limit": resolved.HistoryLimit,
		"history_count": resolved.HistoryCount,
		"prompt_source": resolved.PromptSource,
		"messages":      resolved.Messages,
		"provider":      publicProviderConfig(resolved.Provider),
	})
}

type chatOutcome struct {
	Resolved resolvedChat
	Result   llm.Result
}

// runChat — один ход диалога: реплика игрока (или событие) → модель → ответ; всё пишется в SQLite и журнал запросов.
func (s *Server) runChat(ctx context.Context, sessionID string, in chatInput, allowOverrides bool, maxTokens int) (chatOutcome, error) {
	resolved, err := s.resolveChatRequest(ctx, sessionID, in, allowOverrides)
	if err != nil {
		return chatOutcome{}, chatError(err)
	}
	out := chatOutcome{Resolved: resolved}

	if len(in.GameState) > 0 {
		_ = s.store.UpdateSessionState(ctx, sessionID, in.GameState)
	}
	var userMessageID int64
	if in.UserMessage != "" {
		userMessageID, err = s.store.AddMessageReturningID(ctx, db.Message{SessionID: sessionID, Role: "user", Content: in.UserMessage})
		if err != nil {
			return out, err
		}
	}
	if in.ClientSeq != 0 {
		payload, _ := json.Marshal(map[string]string{"event": in.Event})
		_ = s.store.AddEvent(ctx, sessionID, in.ClientSeq, "chat", payload)
	}

	provider := resolved.Provider
	if maxTokens > 0 && provider.MaxTokens == 0 {
		provider.MaxTokens = maxTokens
	}
	requestMessagesJSON, _ := json.Marshal(resolved.Messages)
	result, llmErr := s.llm.Chat(ctx, provider, resolved.Messages)
	// Клиент мог не дождаться модели и отключиться — журнал всё равно пишем, иначе медленную модель не найти.
	ctx = context.WithoutCancel(ctx)
	if llmErr == nil {
		result.Text = cleanReply(result.Text)
		if result.Text == "" {
			llmErr = errors.New("модель вернула пустой ответ")
		}
	}
	out.Result = result

	endpoint := provider.BaseURL
	if provider.Path != "" {
		endpoint = strings.TrimRight(provider.BaseURL, "/") + "/" + strings.TrimLeft(provider.Path, "/")
	}
	logRow := db.LLMRequest{
		InstallationID: resolved.Session.InstallationID,
		SessionID:      sessionID, RequestMode: resolved.RequestMode,
		Provider: result.Provider, Model: result.Model, Endpoint: endpoint,
		PromptTokens: result.Usage.PromptTokens, CompletionTokens: result.Usage.CompletionTokens, TotalTokens: result.Usage.TotalTokens,
		LatencyMS: result.LatencyMS, TokensPerSecond: result.Performance.TokensPerSecond,
		TimeToFirstTokenMS: int64(result.Performance.TimeToFirstTokenSeconds * 1000),
		Status:             "ok", UserMessageID: userMessageID, UserMessage: in.UserMessage,
		RequestMessages: requestMessagesJSON, HistoryCount: resolved.HistoryCount,
	}
	if logRow.Provider == "" {
		logRow.Provider = provider.Mode
	}
	if logRow.Model == "" {
		logRow.Model = provider.Model
	}
	if llmErr != nil {
		logRow.Status = "error"
		logRow.ErrorText = llmErr.Error()
		_ = s.store.AddLLMRequest(ctx, logRow)
		return out, fail(http.StatusBadGateway, "MODEL_UNAVAILABLE", llmErr.Error())
	}

	assistantMessageID, err := s.store.AddMessageReturningID(ctx, db.Message{
		SessionID: sessionID, Role: "assistant", Content: result.Text, Provider: result.Provider, Model: result.Model,
	})
	if err != nil {
		logRow.Status = "error"
		logRow.ErrorText = "model replied but assistant message could not be stored: " + err.Error()
		logRow.AssistantMessage = result.Text
		_ = s.store.AddLLMRequest(ctx, logRow)
		return out, err
	}
	logRow.AssistantMessageID = assistantMessageID
	logRow.AssistantMessage = result.Text
	_ = s.store.AddLLMRequest(ctx, logRow)
	return out, nil
}

// chatWith — отладочный чат панели: тот же ход, что в игре, плюс подробности запроса для разбора.
func (s *Server) chatWith(w http.ResponseWriter, r *http.Request, allowOverrides bool) {
	var in chatInput
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if in.UserMessage == "" && in.Event == "" && len(in.RawMessages) == 0 {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Нужно сообщение, событие или raw_messages"))
		return
	}
	out, err := s.runChat(r.Context(), r.PathValue("id"), in, allowOverrides, gameMaxTokens)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	resolved, result := out.Resolved, out.Result
	writeJSON(w, 200, map[string]any{
		"reply": result.Text, "provider": result.Provider, "model": result.Model,
		"request_mode": resolved.RequestMode, "usage": result.Usage,
		"performance": result.Performance, "latency_ms": result.LatencyMS,
		"debug": map[string]any{
			"history_limit":     resolved.HistoryLimit,
			"history_count":     resolved.HistoryCount,
			"prompt_source":     resolved.PromptSource,
			"resolved_messages": resolved.Messages,
			"provider":          publicProviderConfig(resolved.Provider),
		},
	})
}

// gameChat — переписка в игре. Игрок присылает только свой текст или ключ события и состояние ночи:
// промпт, историю и провайдера сервер берёт сам, а в ответ отдаёт только текст сообщения.
func (s *Server) gameChat(w http.ResponseWriter, r *http.Request, a *Actor) {
	sessionID := r.PathValue("id")
	if _, err := s.ownSession(r, a, sessionID); err != nil {
		writeAPIError(w, err)
		return
	}
	var in struct {
		Message   string    `json:"message"`
		Event     string    `json:"event"`
		State     gameState `json:"state"`
		ClientSeq int64     `json:"client_seq"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	in.Message = sanitizePlayerText(in.Message)
	if (in.Message == "") == (in.Event == "") {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Нужно что-то одно: message или event"))
		return
	}
	if !s.chatLimit.allow(a.User.ID, time.Now()) {
		writeAPIError(w, fail(http.StatusTooManyRequests, "RATE_LIMITED", "Слишком много сообщений. Подождите немного."))
		return
	}
	state, _ := json.Marshal(in.State)
	ctx, cancel := context.WithTimeout(r.Context(), gameChatTimeout)
	defer cancel()
	out, err := s.runChat(ctx, sessionID, chatInput{
		UserMessage: in.Message, Event: in.Event, GameState: state, ClientSeq: in.ClientSeq, HistoryLimit: gameHistoryLimit,
	}, false, gameMaxTokens)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"reply": out.Result.Text})
}

// rateLimiter — скользящее окно в минуту на пользователя, в памяти процесса.
type rateLimiter struct {
	mu    sync.Mutex
	limit int
	hits  map[string][]time.Time
}

func newRateLimiter(perMinute int) *rateLimiter {
	return &rateLimiter{limit: perMinute, hits: map[string][]time.Time{}}
}

func (l *rateLimiter) allow(key string, now time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	cutoff := now.Add(-time.Minute)
	recent := l.hits[key][:0]
	for _, t := range l.hits[key] {
		if t.After(cutoff) {
			recent = append(recent, t)
		}
	}
	if len(recent) >= l.limit {
		l.hits[key] = recent
		return false
	}
	l.hits[key] = append(recent, now)
	return true
}

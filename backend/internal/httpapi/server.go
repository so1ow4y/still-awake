package httpapi

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"net/url"
	"strconv"
	"strings"

	"still-awake-backend/internal/db"
	"still-awake-backend/internal/frontend"
	"still-awake-backend/internal/llm"
)

type Config struct {
	AdminToken                   string
	AnalyticsToken               string
	DefaultProvider              llm.Config
	AllowClientProviderOverrides bool
	ProviderAllowlist            []string
	AllowUnsafeProviderURLs      bool
	AdminAllowSQLWrites          bool
	DefaultSystemPrompt          string
}

type Server struct {
	store *db.Store
	llm   *llm.Client
	cfg   Config
	log   *slog.Logger
}

func New(store *db.Store, cfg Config, logger *slog.Logger) *Server {
	if logger == nil {
		logger = slog.Default()
	}
	if cfg.DefaultSystemPrompt == "" {
		cfg.DefaultSystemPrompt = "You are the stalker character in Still Awake?. Stay concise, unsettling, non-omniscient, and consistent with the supplied game state. Do not invent physical game events; only produce dialogue."
	}
	return &Server{store: store, llm: llm.New(), cfg: cfg, log: logger}
}

func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, http.StatusOK, map[string]any{"ok": true}) })
	mux.HandleFunc("POST /v1/installations", s.createInstallation)
	mux.HandleFunc("POST /v1/sessions", s.createSession)
	mux.HandleFunc("GET /v1/sessions/{id}", s.getSession)
	mux.HandleFunc("POST /v1/sessions/{id}/events", s.addEvent)
	mux.HandleFunc("GET /v1/sessions/{id}/messages", s.listMessages)
	mux.HandleFunc("POST /v1/sessions/{id}/chat", s.chat)
	mux.HandleFunc("POST /v1/sessions/{id}/prompt-preview", s.promptPreview)
	mux.HandleFunc("GET /v1/admin/overview", s.adminOverview)
	mux.HandleFunc("GET /v1/admin/installations", s.adminListInstallations)
	mux.HandleFunc("PATCH /v1/admin/installations/{id}", s.adminUpdateInstallation)
	mux.HandleFunc("DELETE /v1/admin/installations/{id}", s.adminDeleteInstallation)
	mux.HandleFunc("GET /v1/admin/sessions", s.adminListSessions)
	mux.HandleFunc("PATCH /v1/admin/sessions/{id}", s.adminUpdateSession)
	mux.HandleFunc("DELETE /v1/admin/sessions/{id}", s.adminDeleteSession)
	mux.HandleFunc("GET /v1/admin/sessions/{id}/messages", s.adminListMessages)
	mux.HandleFunc("GET /v1/admin/sessions/{id}/events", s.adminListEvents)
	mux.HandleFunc("PATCH /v1/admin/messages/{id}", s.adminUpdateMessage)
	mux.HandleFunc("DELETE /v1/admin/messages/{id}", s.adminDeleteMessage)
	mux.HandleFunc("PATCH /v1/admin/events/{id}", s.adminUpdateEvent)
	mux.HandleFunc("DELETE /v1/admin/events/{id}", s.adminDeleteEvent)
	mux.HandleFunc("POST /v1/debug/provider/models", s.debugProviderModels)
	mux.HandleFunc("POST /v1/debug/provider/status", s.debugProviderStatus)
	mux.HandleFunc("GET /v1/admin/llm-requests", s.adminListLLMRequests)
	mux.HandleFunc("GET /v1/admin/sessions/{id}/llm-requests", s.adminListSessionLLMRequests)
	mux.HandleFunc("DELETE /v1/admin/llm-requests/{id}", s.adminDeleteLLMRequest)
	mux.HandleFunc("GET /v1/admin/db/schema", s.adminDBSchema)
	mux.HandleFunc("POST /v1/admin/db/query", s.adminDBQuery)
	mux.HandleFunc("GET /v1/analytics/summary", s.analyticsSummary)
	mux.Handle("/", frontend.Handler())
	return logging(s.log, mux)
}

func (s *Server) createInstallation(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Metadata json.RawMessage `json:"metadata"`
	}
	if err := decodeJSON(r, &in); err != nil && !errors.Is(err, context.Canceled) {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	id := newID("inst")
	if err := s.store.CreateInstallation(r.Context(), id, in.Metadata); err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, http.StatusCreated, map[string]string{"installation_id": id})
}

func (s *Server) createSession(w http.ResponseWriter, r *http.Request) {
	var in struct {
		InstallationID string          `json:"installation_id"`
		GameVersion    string          `json:"game_version"`
		Scenario       string          `json:"scenario"`
		State          json.RawMessage `json:"state"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if in.InstallationID == "" {
		writeError(w, 400, errors.New("installation_id is required"))
		return
	}
	if in.Scenario == "" {
		in.Scenario = "night-1"
	}
	v := db.Session{ID: newID("sess"), InstallationID: in.InstallationID, GameVersion: in.GameVersion, Scenario: in.Scenario, State: in.State}
	if err := s.store.CreateSession(r.Context(), v); err != nil {
		writeError(w, 400, err)
		return
	}
	writeJSON(w, http.StatusCreated, map[string]string{"session_id": v.ID})
}

func (s *Server) getSession(w http.ResponseWriter, r *http.Request) {
	v, err := s.store.GetSession(r.Context(), r.PathValue("id"))
	if errors.Is(err, sql.ErrNoRows) {
		writeError(w, 404, errors.New("session not found"))
		return
	}
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, v)
}

func (s *Server) addEvent(w http.ResponseWriter, r *http.Request) {
	id := r.PathValue("id")
	var in struct {
		ClientSeq int64           `json:"client_seq"`
		Type      string          `json:"type"`
		Payload   json.RawMessage `json:"payload"`
		State     json.RawMessage `json:"state"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if in.Type == "" {
		writeError(w, 400, errors.New("type is required"))
		return
	}
	if err := s.store.AddEvent(r.Context(), id, in.ClientSeq, in.Type, in.Payload); err != nil {
		writeError(w, 400, err)
		return
	}
	if len(in.State) > 0 {
		_ = s.store.UpdateSessionState(r.Context(), id, in.State)
	}
	writeJSON(w, 202, map[string]any{"ok": true})
}

func (s *Server) listMessages(w http.ResponseWriter, r *http.Request) {
	msgs, err := s.store.ListMessages(r.Context(), r.PathValue("id"), 100)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, msgs)
}

type chatInput struct {
	UserMessage  string          `json:"user_message"`
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
	return nil
}

func (s *Server) resolveChatRequest(ctx context.Context, sessionID string, in chatInput) (resolvedChat, error) {
	var out resolvedChat
	if err := normalizeChatInput(&in); err != nil {
		return out, err
	}
	session, err := s.store.GetSession(ctx, sessionID)
	if err != nil {
		return out, errors.New("session not found")
	}

	cfg := s.cfg.DefaultProvider
	if in.Provider != nil {
		if !s.cfg.AllowClientProviderOverrides {
			return out, errors.New("client provider overrides are disabled")
		}
		cfg = *in.Provider
		if err := s.validateProvider(cfg); err != nil {
			return out, err
		}
	}

	prompt := s.cfg.DefaultSystemPrompt
	if in.SystemPrompt != "" && s.cfg.AllowClientProviderOverrides {
		prompt = in.SystemPrompt
	}

	messages := []llm.Message{{Role: "system", Content: prompt}}
	historyCount := 0
	if len(in.RawMessages) > 0 && s.cfg.AllowClientProviderOverrides {
		messages = append(messages, in.RawMessages...)
		historyCount = len(in.RawMessages)
	} else {
		history, err := s.store.ListMessages(ctx, sessionID, in.HistoryLimit)
		if err != nil {
			return out, err
		}
		historyCount = len(history)
		for _, m := range history {
			messages = append(messages, llm.Message{Role: m.Role, Content: m.Content})
		}
	}
	if len(in.GameState) > 0 || len(in.Context) > 0 {
		ctxText := fmt.Sprintf("GAME_STATE=%s\nEXTRA_CONTEXT=%s", compactJSON(in.GameState), compactJSON(in.Context))
		messages = append(messages, llm.Message{Role: "system", Content: ctxText})
	}
	if in.UserMessage != "" {
		messages = append(messages, llm.Message{Role: "user", Content: in.UserMessage})
	}
	out = resolvedChat{
		Session: session, Provider: cfg, Messages: messages,
		HistoryCount: historyCount, HistoryLimit: in.HistoryLimit, RequestMode: in.RequestMode,
	}
	return out, nil
}

func publicProviderConfig(cfg llm.Config) map[string]any {
	return map[string]any{
		"mode": cfg.Mode, "base_url": cfg.BaseURL, "path": cfg.Path,
		"model": cfg.Model, "temperature": cfg.Temperature, "api_key_set": cfg.APIKey != "",
	}
}

func (s *Server) promptPreview(w http.ResponseWriter, r *http.Request) {
	var in chatInput
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	resolved, err := s.resolveChatRequest(r.Context(), r.PathValue("id"), in)
	if err != nil {
		status := http.StatusBadRequest
		if err.Error() == "session not found" {
			status = http.StatusNotFound
		}
		writeError(w, status, err)
		return
	}
	writeJSON(w, 200, map[string]any{
		"request_mode":  resolved.RequestMode,
		"history_limit": resolved.HistoryLimit,
		"history_count": resolved.HistoryCount,
		"messages":      resolved.Messages,
		"provider":      publicProviderConfig(resolved.Provider),
	})
}

func (s *Server) chat(w http.ResponseWriter, r *http.Request) {
	sessionID := r.PathValue("id")
	var in chatInput
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if in.UserMessage == "" && len(in.RawMessages) == 0 {
		writeError(w, 400, errors.New("user_message or raw_messages is required"))
		return
	}
	resolved, err := s.resolveChatRequest(r.Context(), sessionID, in)
	if err != nil {
		status := http.StatusBadRequest
		if strings.Contains(err.Error(), "reserved for a future") {
			status = http.StatusNotImplemented
		} else if err.Error() == "client provider overrides are disabled" {
			status = http.StatusForbidden
		} else if err.Error() == "session not found" {
			status = http.StatusNotFound
		}
		writeError(w, status, err)
		return
	}

	if len(in.GameState) > 0 {
		_ = s.store.UpdateSessionState(r.Context(), sessionID, in.GameState)
	}
	var userMessageID int64
	if in.UserMessage != "" {
		userMessageID, err = s.store.AddMessageReturningID(r.Context(), db.Message{SessionID: sessionID, Role: "user", Content: in.UserMessage})
		if err != nil {
			writeError(w, 500, err)
			return
		}
	}
	if in.ClientSeq != 0 {
		_ = s.store.AddEvent(r.Context(), sessionID, in.ClientSeq, "chat", json.RawMessage(`{}`))
	}

	requestMessagesJSON, _ := json.Marshal(resolved.Messages)
	result, llmErr := s.llm.Chat(r.Context(), resolved.Provider, resolved.Messages)
	endpoint := resolved.Provider.BaseURL
	if resolved.Provider.Path != "" {
		endpoint = strings.TrimRight(resolved.Provider.BaseURL, "/") + "/" + strings.TrimLeft(resolved.Provider.Path, "/")
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
		logRow.Provider = resolved.Provider.Mode
	}
	if logRow.Model == "" {
		logRow.Model = resolved.Provider.Model
	}
	if llmErr != nil {
		logRow.Status = "error"
		logRow.ErrorText = llmErr.Error()
		_ = s.store.AddLLMRequest(r.Context(), logRow)
		writeError(w, 502, llmErr)
		return
	}

	assistantMessageID, err := s.store.AddMessageReturningID(r.Context(), db.Message{
		SessionID: sessionID, Role: "assistant", Content: result.Text, Provider: result.Provider, Model: result.Model,
	})
	if err != nil {
		logRow.Status = "error"
		logRow.ErrorText = "model replied but assistant message could not be stored: " + err.Error()
		logRow.AssistantMessage = result.Text
		_ = s.store.AddLLMRequest(r.Context(), logRow)
		writeError(w, 500, err)
		return
	}
	logRow.AssistantMessageID = assistantMessageID
	logRow.AssistantMessage = result.Text
	_ = s.store.AddLLMRequest(r.Context(), logRow)

	writeJSON(w, 200, map[string]any{
		"reply": result.Text, "provider": result.Provider, "model": result.Model,
		"request_mode": resolved.RequestMode, "usage": result.Usage,
		"performance": result.Performance, "latency_ms": result.LatencyMS,
		"debug": map[string]any{
			"history_limit":     resolved.HistoryLimit,
			"history_count":     resolved.HistoryCount,
			"resolved_messages": resolved.Messages,
			"provider":          publicProviderConfig(resolved.Provider),
		},
	})
}

func (s *Server) requireAdmin(w http.ResponseWriter, r *http.Request) bool {
	if !s.adminOK(r) {
		writeError(w, http.StatusUnauthorized, errors.New("unauthorized"))
		return false
	}
	return true
}

func (s *Server) adminOverview(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	v, err := s.store.Overview(r.Context())
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{
		"counts": v,
		"config": map[string]any{
			"client_provider_overrides":  s.cfg.AllowClientProviderOverrides,
			"unsafe_provider_urls":       s.cfg.AllowUnsafeProviderURLs,
			"provider_allowlist":         s.cfg.ProviderAllowlist,
			"default_provider_mode":      s.cfg.DefaultProvider.Mode,
			"default_provider_base_url":  s.cfg.DefaultProvider.BaseURL,
			"default_provider_model":     s.cfg.DefaultProvider.Model,
			"server_has_default_api_key": s.cfg.DefaultProvider.APIKey != "",
			"admin_sql_writes":           s.cfg.AdminAllowSQLWrites,
			"analytics_token_configured": s.cfg.AnalyticsToken != "",
		},
	})
}

func (s *Server) adminListInstallations(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.SearchInstallations(r.Context(), r.URL.Query().Get("q"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminUpdateInstallation(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	var in struct {
		Metadata json.RawMessage `json:"metadata"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if err := s.store.UpdateInstallation(r.Context(), r.PathValue("id"), in.Metadata); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("installation not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{"ok": true})
}

func (s *Server) adminDeleteInstallation(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	if err := s.store.DeleteInstallation(r.Context(), r.PathValue("id")); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("installation not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) adminUpdateSession(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	var in struct {
		GameVersion string          `json:"game_version"`
		Scenario    string          `json:"scenario"`
		State       json.RawMessage `json:"state"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if err := s.store.UpdateSession(r.Context(), r.PathValue("id"), in.GameVersion, in.Scenario, in.State); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("session not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{"ok": true})
}

func (s *Server) adminListMessages(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListMessages(r.Context(), r.PathValue("id"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminListEvents(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListEvents(r.Context(), r.PathValue("id"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func pathInt64(r *http.Request) (int64, error) {
	return strconv.ParseInt(r.PathValue("id"), 10, 64)
}

func (s *Server) adminUpdateMessage(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	id, err := pathInt64(r)
	if err != nil {
		writeError(w, 400, errors.New("invalid message id"))
		return
	}
	var in struct{ Role, Content, Provider, Model string }
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if err := s.store.UpdateMessage(r.Context(), id, in.Role, in.Content, in.Provider, in.Model); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("message not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{"ok": true})
}

func (s *Server) adminDeleteMessage(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	id, err := pathInt64(r)
	if err != nil {
		writeError(w, 400, errors.New("invalid message id"))
		return
	}
	if err := s.store.DeleteMessage(r.Context(), id); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("message not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) adminUpdateEvent(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	id, err := pathInt64(r)
	if err != nil {
		writeError(w, 400, errors.New("invalid event id"))
		return
	}
	var in struct {
		ClientSeq int64           `json:"client_seq"`
		Type      string          `json:"type"`
		Payload   json.RawMessage `json:"payload"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if err := s.store.UpdateEvent(r.Context(), id, in.ClientSeq, in.Type, in.Payload); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("event not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{"ok": true})
}

func (s *Server) adminDeleteEvent(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	id, err := pathInt64(r)
	if err != nil {
		writeError(w, 400, errors.New("invalid event id"))
		return
	}
	if err := s.store.DeleteEvent(r.Context(), id); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("event not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) adminListSessions(w http.ResponseWriter, r *http.Request) {
	if !s.adminOK(r) {
		writeError(w, 401, errors.New("unauthorized"))
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.SearchSessions(r.Context(), r.URL.Query().Get("q"), r.URL.Query().Get("installation_id"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminDeleteSession(w http.ResponseWriter, r *http.Request) {
	if !s.adminOK(r) {
		writeError(w, 401, errors.New("unauthorized"))
		return
	}
	if err := s.store.DeleteSession(r.Context(), r.PathValue("id")); err != nil {
		writeError(w, 404, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) adminOK(r *http.Request) bool {
	return s.cfg.AdminToken != "" && r.Header.Get("X-Admin-Token") == s.cfg.AdminToken
}

func (s *Server) validateProvider(cfg llm.Config) error {
	if strings.ToLower(cfg.Mode) == "mock" || cfg.Mode == "" {
		return nil
	}
	if strings.ToLower(cfg.Mode) != "openai_compatible" {
		return errors.New("only mock and openai_compatible are supported")
	}
	u, err := url.Parse(cfg.BaseURL)
	if err != nil || u.Host == "" {
		return errors.New("invalid provider base_url")
	}
	if s.cfg.AllowUnsafeProviderURLs {
		return nil
	}
	for _, allowed := range s.cfg.ProviderAllowlist {
		if strings.EqualFold(strings.TrimRight(allowed, "/"), strings.TrimRight(cfg.BaseURL, "/")) {
			return nil
		}
	}
	return errors.New("provider base_url is not in PROVIDER_ALLOWLIST")
}

func newID(prefix string) string {
	b := make([]byte, 16)
	_, _ = rand.Read(b)
	return prefix + "_" + hex.EncodeToString(b)
}

func compactJSON(v json.RawMessage) string {
	if len(v) == 0 {
		return "{}"
	}
	var x any
	if json.Unmarshal(v, &x) != nil {
		return string(v)
	}
	b, _ := json.Marshal(x)
	return string(b)
}

func decodeJSON(r *http.Request, dst any) error {
	dec := json.NewDecoder(http.MaxBytesReader(nilResponseWriter{}, r.Body, 1<<20))
	dec.DisallowUnknownFields()
	return dec.Decode(dst)
}

type nilResponseWriter struct{}

func (nilResponseWriter) Header() http.Header       { return make(http.Header) }
func (nilResponseWriter) Write([]byte) (int, error) { return 0, nil }
func (nilResponseWriter) WriteHeader(int)           {}

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}
func writeError(w http.ResponseWriter, status int, err error) {
	writeJSON(w, status, map[string]string{"error": err.Error()})
}

func logging(log *slog.Logger, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		log.Info("http", "method", r.Method, "path", r.URL.Path)
		next.ServeHTTP(w, r)
	})
}

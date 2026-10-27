package httpapi

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
	"still-awake-backend/internal/frontend"
	"still-awake-backend/internal/llm"
)

type Config struct {
	DefaultProvider              llm.Config
	AllowClientProviderOverrides bool
	ProviderAllowlist            []string
	AllowUnsafeProviderURLs      bool
	AdminAllowSQLWrites          bool
	// Срок сессии панели (продлевается при активности) и игрового токена.
	SessionTTL     time.Duration
	GameSessionTTL time.Duration
	// Папка с файлами панели на диске (для правки интерфейса без пересборки); пусто — встроенные файлы.
	FrontendDir string
}

type Server struct {
	store     *db.Store
	llm       *llm.Client
	cfg       Config
	log       *slog.Logger
	chatLimit *rateLimiter
	// registerLimit — регистраций из игры с одного IP в минуту.
	registerLimit *rateLimiter
}

func New(store *db.Store, cfg Config, logger *slog.Logger) *Server {
	if logger == nil {
		logger = slog.Default()
	}
	if cfg.SessionTTL <= 0 {
		cfg.SessionTTL = 7 * 24 * time.Hour
	}
	if cfg.GameSessionTTL <= 0 {
		cfg.GameSessionTTL = 365 * 24 * time.Hour
	}
	return &Server{store: store, llm: llm.New(), cfg: cfg, log: logger,
		chatLimit: newRateLimiter(gameChatPerMinute), registerLimit: newRateLimiter(maxRegistrationsPerWindow)}
}

// plain — обработчик, которому актор не нужен (права уже проверены обёрткой).
func plain(h http.HandlerFunc) actorHandler {
	return func(w http.ResponseWriter, r *http.Request, _ *Actor) { h(w, r) }
}

func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	need := s.need
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, http.StatusOK, map[string]any{"ok": true}) })

	// Вход в панель и свой профиль.
	mux.HandleFunc("POST /v1/auth/login", s.login)
	mux.HandleFunc("POST /v1/auth/logout", s.authed(s.logout))
	mux.HandleFunc("GET /v1/auth/me", s.authed(s.me))
	mux.HandleFunc("PATCH /v1/auth/me", s.authed(s.updateMe))
	mux.HandleFunc("POST /v1/auth/me/username", s.authed(s.changeMyUsername))
	mux.HandleFunc("POST /v1/auth/me/password", s.authed(s.changeMyPassword))
	mux.HandleFunc("GET /v1/auth/me/sessions", s.authed(s.mySessions))
	mux.HandleFunc("DELETE /v1/auth/me/sessions/{id}", s.authed(s.revokeMySession))
	mux.HandleFunc("POST /v1/auth/me/sessions/revoke-others", s.authed(s.revokeMyOtherSessions))
	mux.HandleFunc("GET /v1/auth/me/logins", s.authed(s.myLogins))
	mux.HandleFunc("GET /v1/auth/me/usernames", s.authed(s.myUsernames))

	// Игра: регистрация по телефону и игровые сессии.
	mux.HandleFunc("POST /v1/game/register", s.gameRegister)
	mux.HandleFunc("GET /v1/game/me", need(auth.PermGamePlay)(s.gameMe))
	mux.HandleFunc("GET /v1/game/model", need(auth.PermGamePlay)(s.gameModel))
	mux.HandleFunc("PATCH /v1/game/me", need(auth.PermGamePlay)(s.gameUpdateMe))
	mux.HandleFunc("POST /v1/game/logout", need(auth.PermGamePlay)(s.gameLogout))
	mux.HandleFunc("POST /v1/game/sessions", need(auth.PermGamePlay)(s.gameCreateSession))
	mux.HandleFunc("POST /v1/game/sessions/{id}/events", need(auth.PermGamePlay)(s.gameAddEvent))
	mux.HandleFunc("POST /v1/game/sessions/{id}/chat", need(auth.PermGamePlay)(s.gameChat))

	// Пользователи, роли, журнал входов.
	mux.HandleFunc("GET /v1/admin/users", need(auth.PermUsersRead)(s.listUsers))
	mux.HandleFunc("POST /v1/admin/users", need(auth.PermUsersCreate)(s.createUser))
	mux.HandleFunc("GET /v1/admin/users/{id}", need(auth.PermUsersRead)(s.getUserCard))
	mux.HandleFunc("PATCH /v1/admin/users/{id}", need(auth.PermUsersUpdate)(s.updateUser))
	mux.HandleFunc("POST /v1/admin/users/{id}/password", need(auth.PermUsersUpdate)(s.setUserPassword))
	mux.HandleFunc("POST /v1/admin/users/{id}/block", need(auth.PermUsersBlock)(s.blockUser))
	mux.HandleFunc("POST /v1/admin/users/{id}/unblock", need(auth.PermUsersBlock)(s.unblockUser))
	mux.HandleFunc("POST /v1/admin/users/{id}/logout", need(auth.PermUsersBlock)(s.logoutUser))
	mux.HandleFunc("PUT /v1/admin/users/{id}/roles", need(auth.PermRolesAssign)(s.setUserRoles))
	mux.HandleFunc("GET /v1/admin/roles", need(auth.PermRolesRead, auth.PermRolesAssign, auth.PermRolesManage)(s.listRoles))
	mux.HandleFunc("GET /v1/admin/permissions", need(auth.PermRolesRead, auth.PermRolesManage)(s.listPermissions))
	mux.HandleFunc("POST /v1/admin/roles", need(auth.PermRolesManage)(s.createRole))
	mux.HandleFunc("PATCH /v1/admin/roles/{id}", need(auth.PermRolesManage)(s.updateRole))
	mux.HandleFunc("DELETE /v1/admin/roles/{id}", need(auth.PermRolesManage)(s.deleteRole))
	mux.HandleFunc("GET /v1/admin/logins", need(auth.PermLoginsRead)(s.listLogins))

	// Модель: провайдер, системный промпт.
	llmView := []string{auth.PermLLMSettings, auth.PermLLMDebug, auth.PermLLMPrompt}
	mux.HandleFunc("GET /v1/llm/provider", need(llmView...)(s.getProvider))
	mux.HandleFunc("PUT /v1/llm/provider", need(auth.PermLLMSettings)(s.putProvider))
	mux.HandleFunc("POST /v1/llm/provider/models", need(auth.PermLLMSettings, auth.PermLLMDebug)(s.providerModels))
	mux.HandleFunc("POST /v1/llm/provider/status", need(llmView...)(s.providerStatus))
	mux.HandleFunc("GET /v1/llm/prompt", need(llmView...)(s.getPrompt))
	mux.HandleFunc("POST /v1/llm/prompt", need(auth.PermLLMPrompt)(s.savePrompt))
	mux.HandleFunc("GET /v1/llm/prompt/versions", need(auth.PermLLMPrompt, auth.PermLLMDebug)(s.listPromptVersions))
	mux.HandleFunc("POST /v1/llm/prompt/versions/{id}/activate", need(auth.PermLLMPrompt)(s.activatePromptVersion))

	// Лаборатория: отладочный чат от имени тестовой установки.
	debug := need(auth.PermLLMDebug)
	mux.HandleFunc("POST /v1/installations", debug(plain(s.createInstallation)))
	mux.HandleFunc("POST /v1/sessions", debug(plain(s.createSession)))
	mux.HandleFunc("GET /v1/sessions/{id}", debug(plain(s.getSession)))
	mux.HandleFunc("POST /v1/sessions/{id}/events", debug(plain(s.addEvent)))
	mux.HandleFunc("GET /v1/sessions/{id}/messages", debug(plain(s.listMessages)))
	mux.HandleFunc("POST /v1/sessions/{id}/chat", debug(func(w http.ResponseWriter, r *http.Request, a *Actor) { s.chatWith(w, r, s.overridesAllowed(a)) }))
	mux.HandleFunc("POST /v1/sessions/{id}/prompt-preview", debug(func(w http.ResponseWriter, r *http.Request, a *Actor) { s.promptPreview(w, r, s.overridesAllowed(a)) }))

	// Игровые данные.
	read, manage := need(auth.PermSessionsRead), need(auth.PermSessionsManage)
	mux.HandleFunc("GET /v1/admin/overview", s.authed(s.adminOverview))
	mux.HandleFunc("GET /v1/admin/installations", read(plain(s.adminListInstallations)))
	mux.HandleFunc("PATCH /v1/admin/installations/{id}", manage(plain(s.adminUpdateInstallation)))
	mux.HandleFunc("DELETE /v1/admin/installations/{id}", manage(plain(s.adminDeleteInstallation)))
	mux.HandleFunc("GET /v1/admin/sessions", read(plain(s.adminListSessions)))
	mux.HandleFunc("GET /v1/admin/sessions/{id}", read(plain(s.getSession)))
	mux.HandleFunc("PATCH /v1/admin/sessions/{id}", manage(plain(s.adminUpdateSession)))
	mux.HandleFunc("DELETE /v1/admin/sessions/{id}", manage(plain(s.adminDeleteSession)))
	mux.HandleFunc("GET /v1/admin/sessions/{id}/messages", read(plain(s.adminListMessages)))
	mux.HandleFunc("GET /v1/admin/sessions/{id}/events", read(plain(s.adminListEvents)))
	mux.HandleFunc("GET /v1/admin/sessions/{id}/llm-requests", read(plain(s.adminListSessionLLMRequests)))
	mux.HandleFunc("PATCH /v1/admin/messages/{id}", manage(plain(s.adminUpdateMessage)))
	mux.HandleFunc("DELETE /v1/admin/messages/{id}", manage(plain(s.adminDeleteMessage)))
	mux.HandleFunc("PATCH /v1/admin/events/{id}", manage(plain(s.adminUpdateEvent)))
	mux.HandleFunc("DELETE /v1/admin/events/{id}", manage(plain(s.adminDeleteEvent)))
	mux.HandleFunc("GET /v1/admin/llm-requests", read(plain(s.adminListLLMRequests)))
	mux.HandleFunc("DELETE /v1/admin/llm-requests/{id}", manage(plain(s.adminDeleteLLMRequest)))
	mux.HandleFunc("GET /v1/admin/db/schema", need(auth.PermDBRead)(plain(s.adminDBSchema)))
	mux.HandleFunc("POST /v1/admin/db/query", need(auth.PermDBRead)(s.adminDBQuery))
	mux.HandleFunc("GET /v1/analytics/summary", need(auth.PermAnalyticsRead)(plain(s.analyticsSummary)))

	mux.Handle("/", frontend.Handler(s.cfg.FrontendDir))
	return logging(s.log, mux)
}

// overridesAllowed — можно ли в отладочном запросе подменить провайдер, промпт и историю.
func (s *Server) overridesAllowed(a *Actor) bool {
	return s.cfg.AllowClientProviderOverrides && a.Can(auth.PermLLMDebug)
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
	if err := s.store.AddEvent(r.Context(), id, in.ClientSeq, truncate(in.Type, 64), in.Payload); err != nil {
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

func (s *Server) adminOverview(w http.ResponseWriter, r *http.Request, a *Actor) {
	v, err := s.store.Overview(r.Context())
	if err != nil {
		writeError(w, 500, err)
		return
	}
	_, staff, _ := s.store.ListUsers(r.Context(), db.UserFilter{Kind: db.KindStaff, Limit: 1})
	_, players, _ := s.store.ListUsers(r.Context(), db.UserFilter{Kind: db.KindPlayer, Limit: 1})
	writeJSON(w, 200, map[string]any{
		"counts": v,
		"users":  map[string]int{"staff": staff, "players": players},
		"config": map[string]any{
			"client_provider_overrides": s.cfg.AllowClientProviderOverrides,
			"unsafe_provider_urls":      s.cfg.AllowUnsafeProviderURLs,
			"provider_allowlist":        s.cfg.ProviderAllowlist,
			"admin_sql_writes":          s.cfg.AdminAllowSQLWrites,
		},
	})
}

func (s *Server) adminListInstallations(w http.ResponseWriter, r *http.Request) {
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.SearchInstallations(r.Context(), r.URL.Query().Get("q"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminUpdateInstallation(w http.ResponseWriter, r *http.Request) {
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
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListMessages(r.Context(), r.PathValue("id"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminListEvents(w http.ResponseWriter, r *http.Request) {
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
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.SearchSessions(r.Context(), r.URL.Query().Get("q"), r.URL.Query().Get("installation_id"), limit)
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminDeleteSession(w http.ResponseWriter, r *http.Request) {
	if err := s.store.DeleteSession(r.Context(), r.PathValue("id")); err != nil {
		writeError(w, 404, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
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

func newID(prefix string) string { return auth.NewID(prefix) }

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

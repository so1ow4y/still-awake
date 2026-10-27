package httpapi

import (
	"context"
	"database/sql"
	"errors"
	"net"
	"net/http"
	"sort"
	"strings"
	"time"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

const (
	sessionCookie = "sa_session"
	// Заголовок, без которого не принимаются изменяющие запросы с cookie: чужой сайт не может его
	// подставить без CORS-preflight, а CORS сервер не разрешает — это и есть защита от CSRF.
	csrfHeader = "X-Requested-With"

	loginWindow      = 10 * time.Minute
	loginMaxFailures = 10
	touchEvery       = 5 * time.Minute
)

// Actor — кто делает запрос: пользователь, его права и уровень старшей роли.
type Actor struct {
	User      db.User
	Perms     map[string]bool
	Priority  int
	SessionID string
	ViaCookie bool
}

func (a *Actor) Can(perm string) bool { return a != nil && a.Perms[perm] }

func (a *Actor) CanAny(perms ...string) bool {
	for _, p := range perms {
		if a.Can(p) {
			return true
		}
	}
	return false
}

func (a *Actor) permissionList() []string {
	out := make([]string, 0, len(a.Perms))
	for p := range a.Perms {
		out = append(out, p)
	}
	sort.Strings(out)
	return out
}

type actorHandler func(w http.ResponseWriter, r *http.Request, a *Actor)

type apiError struct {
	status  int
	code    string
	message string
}

func (e *apiError) Error() string { return e.message }

func fail(status int, code, message string) *apiError {
	return &apiError{status: status, code: code, message: message}
}

var (
	errUnauthorized = fail(http.StatusUnauthorized, "UNAUTHORIZED", "Нужно войти")
	errForbidden    = fail(http.StatusForbidden, "FORBIDDEN", "Недостаточно прав для этого действия")
)

func writeAPIError(w http.ResponseWriter, err error) {
	var e *apiError
	if errors.As(err, &e) {
		writeJSON(w, e.status, map[string]string{"error": e.message, "code": e.code})
		return
	}
	switch {
	case errors.Is(err, sql.ErrNoRows):
		writeJSON(w, http.StatusNotFound, map[string]string{"error": "Не найдено", "code": "NOT_FOUND"})
	case errors.Is(err, db.ErrConflict):
		writeJSON(w, http.StatusConflict, map[string]string{"error": "Такое значение уже занято", "code": "ALREADY_EXISTS"})
	default:
		writeJSON(w, http.StatusInternalServerError, map[string]string{"error": err.Error(), "code": "INTERNAL"})
	}
}

// actor находит сессию по cookie панели или по Bearer-токену. nil, nil — запрос без входа.
func (s *Server) actor(r *http.Request) (*Actor, error) {
	token, viaCookie := "", false
	if h := r.Header.Get("Authorization"); strings.HasPrefix(h, "Bearer ") {
		token = strings.TrimSpace(strings.TrimPrefix(h, "Bearer "))
	} else if c, err := r.Cookie(sessionCookie); err == nil {
		token, viaCookie = c.Value, true
	}
	if token == "" {
		return nil, nil
	}
	nowTime := time.Now().UTC()
	sess, err := s.store.ActiveSessionByToken(r.Context(), auth.TokenHash(token), nowTime.Format(time.RFC3339Nano))
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	user, err := s.store.GetUser(r.Context(), sess.UserID)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	if user.Status != db.StatusActive {
		return nil, nil
	}
	perms, priority, err := s.store.UserAccess(r.Context(), user.ID)
	if err != nil {
		return nil, err
	}
	// Скользящий срок: активная сессия продлевается, но не чаще раза в пять минут.
	if last, err := time.Parse(time.RFC3339Nano, sess.LastSeenAt); err == nil && nowTime.Sub(last) > touchEvery {
		_ = s.store.TouchAuthSession(r.Context(), sess.ID, nowTime.Format(time.RFC3339Nano), nowTime.Add(s.ttlFor(sess.Kind)).Format(time.RFC3339Nano))
	}
	return &Actor{User: user, Perms: perms, Priority: priority, SessionID: sess.ID, ViaCookie: viaCookie}, nil
}

func (s *Server) ttlFor(kind string) time.Duration {
	if kind == "game" {
		return s.cfg.GameSessionTTL
	}
	return s.cfg.SessionTTL
}

// authed — нужен вход (любой пользователь). need — нужен вход и хотя бы одно из прав.
func (s *Server) authed(h actorHandler) http.HandlerFunc {
	return s.need()(h)
}

func (s *Server) need(perms ...string) func(actorHandler) http.HandlerFunc {
	return func(h actorHandler) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			a, err := s.actor(r)
			if err != nil {
				writeAPIError(w, err)
				return
			}
			if a == nil {
				writeAPIError(w, errUnauthorized)
				return
			}
			if a.ViaCookie && r.Method != http.MethodGet && r.Method != http.MethodHead && r.Header.Get(csrfHeader) == "" {
				writeAPIError(w, fail(http.StatusForbidden, "CSRF", "Запрос без заголовка "+csrfHeader+" отклонён"))
				return
			}
			if len(perms) > 0 && !a.CanAny(perms...) {
				writeAPIError(w, errForbidden)
				return
			}
			h(w, r, a)
		}
	}
}

// sinceWindow — начало окна, в котором считаются неудачные попытки входа.
func sinceWindow() string { return time.Now().UTC().Add(-loginWindow).Format(time.RFC3339Nano) }

func clientIP(r *http.Request) string {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		return r.RemoteAddr
	}
	return host
}

func userAgent(r *http.Request) string {
	ua := r.UserAgent()
	if len(ua) > 300 {
		ua = ua[:300]
	}
	return ua
}

// openSession создаёт сессию и возвращает токен (в БД остаётся только его хеш).
func (s *Server) openSession(ctx context.Context, r *http.Request, userID, kind string) (string, db.AuthSession, error) {
	token := auth.NewToken()
	nowTime := time.Now().UTC()
	sess := db.AuthSession{
		ID: auth.NewID("ses"), UserID: userID, Kind: kind, IP: clientIP(r), UserAgent: userAgent(r),
		CreatedAt: nowTime.Format(time.RFC3339Nano), ExpiresAt: nowTime.Add(s.ttlFor(kind)).Format(time.RFC3339Nano),
	}
	return token, sess, s.store.CreateAuthSession(ctx, sess, auth.TokenHash(token))
}

func (s *Server) setSessionCookie(w http.ResponseWriter, r *http.Request, token string, maxAge int) {
	http.SetCookie(w, &http.Cookie{
		Name: sessionCookie, Value: token, Path: "/", MaxAge: maxAge,
		HttpOnly: true, SameSite: http.SameSiteStrictMode, Secure: r.TLS != nil,
	})
}

func (s *Server) meView(a *Actor) map[string]any {
	return map[string]any{"user": a.User, "permissions": a.permissionList(), "priority": a.Priority}
}

// -------------------------------------------------------------------- вход и выход

func (s *Server) login(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	ctx := r.Context()
	ip := clientIP(r)
	identifier := strings.TrimSpace(in.Username)
	record := func(userID string, ok bool, reason string) {
		_ = s.store.AddLoginEvent(ctx, db.LoginEvent{UserID: userID, Identifier: identifier, Success: ok, Reason: reason, Method: "password", IP: ip, UserAgent: userAgent(r)})
	}

	if n, err := s.store.RecentLoginFailures(ctx, identifier, ip, sinceWindow()); err == nil && n >= loginMaxFailures {
		record("", false, "rate_limited")
		writeAPIError(w, fail(http.StatusTooManyRequests, "RATE_LIMITED", "Слишком много неудачных попыток. Подождите несколько минут."))
		return
	}

	invalid := fail(http.StatusUnauthorized, "INVALID_CREDENTIALS", "Неверный логин или пароль")
	user, err := s.store.GetUserByUsername(ctx, identifier)
	if errors.Is(err, sql.ErrNoRows) {
		record("", false, "user_not_found")
		writeAPIError(w, invalid)
		return
	}
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if !user.HasPassword || !auth.VerifyPassword(user.PasswordHash, in.Password) {
		record(user.ID, false, "invalid_password")
		writeAPIError(w, invalid)
		return
	}
	if user.Status != db.StatusActive {
		record(user.ID, false, "blocked")
		msg := "Аккаунт заблокирован"
		if user.BlockedReason != "" {
			msg += ": " + user.BlockedReason
		}
		writeAPIError(w, fail(http.StatusForbidden, "BLOCKED", msg))
		return
	}

	token, _, err := s.openSession(ctx, r, user.ID, "web")
	if err != nil {
		writeAPIError(w, err)
		return
	}
	_ = s.store.MarkLogin(ctx, user.ID, ip)
	record(user.ID, true, "")
	s.setSessionCookie(w, r, token, int(s.cfg.SessionTTL.Seconds()))

	r.AddCookie(&http.Cookie{Name: sessionCookie, Value: token})
	a, err := s.actor(r)
	if err != nil || a == nil {
		writeAPIError(w, errUnauthorized)
		return
	}
	writeJSON(w, http.StatusOK, s.meView(a))
}

func (s *Server) logout(w http.ResponseWriter, r *http.Request, a *Actor) {
	_ = s.store.RevokeAuthSession(r.Context(), a.User.ID, a.SessionID, "logout")
	s.setSessionCookie(w, r, "", -1)
	writeJSON(w, http.StatusOK, map[string]any{"ok": true})
}

func (s *Server) me(w http.ResponseWriter, r *http.Request, a *Actor) {
	writeJSON(w, http.StatusOK, s.meView(a))
}

// -------------------------------------------------------------------------- профиль

func (s *Server) updateMe(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		DisplayName string `json:"display_name"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if err := s.store.UpdateUserProfile(r.Context(), a.User.ID, cleanName(in.DisplayName), nil); err != nil {
		writeAPIError(w, err)
		return
	}
	s.respondMe(w, r)
}

// Свой логин может сменить любой пользователь; прежний остаётся в истории.
func (s *Server) changeMyUsername(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		Username string `json:"username"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	username, err := auth.NormalizeUsername(in.Username)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
		return
	}
	if err := s.store.ChangeUsername(r.Context(), a.User.ID, username, a.User.ID); err != nil {
		writeAPIError(w, err)
		return
	}
	s.respondMe(w, r)
}

func (s *Server) changeMyPassword(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		CurrentPassword string `json:"current_password"`
		NewPassword     string `json:"new_password"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if a.User.HasPassword && !auth.VerifyPassword(a.User.PasswordHash, in.CurrentPassword) {
		writeAPIError(w, fail(http.StatusBadRequest, "INVALID_PASSWORD", "Текущий пароль неверный"))
		return
	}
	if in.NewPassword == in.CurrentPassword {
		writeAPIError(w, fail(http.StatusBadRequest, "SAME_PASSWORD", "Новый пароль совпадает с текущим"))
		return
	}
	hash, err := auth.HashPassword(in.NewPassword)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
		return
	}
	if err := s.store.SetPassword(r.Context(), a.User.ID, hash, false); err != nil {
		writeAPIError(w, err)
		return
	}
	// Остальные устройства выходят: пароль мог смениться именно потому, что его узнали.
	revoked, _ := s.store.RevokeUserSessions(r.Context(), a.User.ID, "password_changed", a.SessionID)
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "revoked_sessions": revoked})
}

func (s *Server) mySessions(w http.ResponseWriter, r *http.Request, a *Actor) {
	rows, err := s.store.ListUserSessions(r.Context(), a.User.ID, 50)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	for i := range rows {
		rows[i].Current = rows[i].ID == a.SessionID
	}
	writeJSON(w, http.StatusOK, rows)
}

func (s *Server) revokeMySession(w http.ResponseWriter, r *http.Request, a *Actor) {
	if err := s.store.RevokeAuthSession(r.Context(), a.User.ID, r.PathValue("id"), "manual"); err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true})
}

func (s *Server) revokeMyOtherSessions(w http.ResponseWriter, r *http.Request, a *Actor) {
	n, err := s.store.RevokeUserSessions(r.Context(), a.User.ID, "manual", a.SessionID)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "revoked_sessions": n})
}

func (s *Server) myLogins(w http.ResponseWriter, r *http.Request, a *Actor) {
	rows, err := s.store.ListLoginEvents(r.Context(), db.LoginFilter{UserID: a.User.ID, Limit: 50})
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

func (s *Server) myUsernames(w http.ResponseWriter, r *http.Request, a *Actor) {
	rows, err := s.store.ListUsernameHistory(r.Context(), a.User.ID)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

// respondMe перечитывает актора после изменения профиля.
func (s *Server) respondMe(w http.ResponseWriter, r *http.Request) {
	a, err := s.actor(r)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if a == nil {
		writeAPIError(w, errUnauthorized)
		return
	}
	writeJSON(w, http.StatusOK, s.meView(a))
}

func cleanName(v string) string {
	v = strings.TrimSpace(v)
	if r := []rune(v); len(r) > 64 {
		v = string(r[:64])
	}
	return v
}

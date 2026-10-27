package httpapi

import (
	"database/sql"
	"errors"
	"net/http"
	"strings"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

// Аккаунт игрока: вход в игру по логину и паролю (при каждом запуске — из файла игры), смена пароля из игры
// и «Мои ночи» в панели. Регистрация — gameRegister (game.go).

// gameLogin — вход в игру по логину и паролю. Неверные — 401: игра снова показывает экран входа. Ночи привязаны
// к установке аккаунта, поэтому на любом компьютере продолжаются те же.
func (s *Server) gameLogin(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Username    string `json:"username"`
		Password    string `json:"password"`
		GameVersion string `json:"game_version"`
		Platform    string `json:"platform"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	ctx := r.Context()
	ip := clientIP(r)
	identifier := strings.TrimSpace(in.Username)
	record := func(userID string, ok bool, reason string) {
		_ = s.store.AddLoginEvent(ctx, db.LoginEvent{UserID: userID, Identifier: identifier, Success: ok, Reason: reason, Method: "game_password", IP: ip, UserAgent: userAgent(r)})
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
	if user.Kind != db.KindPlayer {
		record(user.ID, false, "not_player")
		writeAPIError(w, fail(http.StatusForbidden, "NOT_PLAYER", "Это аккаунт сотрудника — в игру входят аккаунтом игрока"))
		return
	}
	if user.Status != db.StatusActive {
		record(user.ID, false, "blocked")
		writeAPIError(w, fail(http.StatusForbidden, "BLOCKED", "Аккаунт заблокирован"))
		return
	}
	if user.InstallationID == "" {
		id, err := s.ensureInstallation(ctx, "", in.Platform, in.GameVersion)
		if err != nil {
			writeAPIError(w, err)
			return
		}
		_ = s.store.SetUserInstallation(ctx, user.ID, id)
	}
	s.finishGameLogin(w, r, user, false, "game_password", identifier)
}

// gameSetPassword — смена пароля из настроек игры: нужен текущий (игра берёт его из своего файла).
// Остальные входы аккаунта завершаются — на других компьютерах игра попросит войти заново.
func (s *Server) gameSetPassword(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		CurrentPassword string `json:"current_password"`
		NewPassword     string `json:"new_password"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if !a.User.HasPassword || !auth.VerifyPassword(a.User.PasswordHash, in.CurrentPassword) {
		writeAPIError(w, fail(http.StatusBadRequest, "INVALID_PASSWORD", "Текущий пароль неверный"))
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
	revoked, _ := s.store.RevokeUserSessions(r.Context(), a.User.ID, "password_changed", a.SessionID)
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "revoked_sessions": revoked})
}

// ---------- «Мои ночи»: игрок в панели видит только свои сессии ----------

// myNights — ночи установки игрока (со сводкой, как в списке панели). Запросы к модели игроку не отдаются:
// в них системный промпт.
func (s *Server) myNights(w http.ResponseWriter, r *http.Request, a *Actor) {
	if a.User.InstallationID == "" {
		writeJSON(w, http.StatusOK, []db.Session{})
		return
	}
	rows, err := s.store.SearchSessions(r.Context(), "", a.User.InstallationID, 500)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

func (s *Server) myNight(w http.ResponseWriter, r *http.Request, a *Actor) {
	sess, err := s.ownSession(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, notFoundIfMissing(err))
		return
	}
	writeJSON(w, http.StatusOK, sess)
}

func (s *Server) myNightMessages(w http.ResponseWriter, r *http.Request, a *Actor) {
	sess, err := s.ownSession(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, notFoundIfMissing(err))
		return
	}
	rows, err := s.store.ListMessages(r.Context(), sess.ID, 200)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

func (s *Server) myNightEvents(w http.ResponseWriter, r *http.Request, a *Actor) {
	sess, err := s.ownSession(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, notFoundIfMissing(err))
		return
	}
	rows, err := s.store.ListEvents(r.Context(), sess.ID, 500)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

// notFoundIfMissing — нет такой сессии и чужая сессия выглядят одинаково: «не найдена».
func notFoundIfMissing(err error) error {
	if errors.Is(err, sql.ErrNoRows) {
		return fail(http.StatusNotFound, "NOT_FOUND", "Сессия не найдена")
	}
	return err
}

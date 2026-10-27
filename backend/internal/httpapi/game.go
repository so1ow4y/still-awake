package httpapi

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"net/http"
	"regexp"
	"strings"
	"time"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

// Игровое API. Игра регистрируется сама, без участия игрока: по ключу установки (installation_id, его
// генерирует игра) или, по желанию, по номеру телефона (без SMS — номер и есть аккаунт). В ответ — долгоживущий
// токен; дальше игра ходит с Authorization: Bearer <токен>.

const maxRegistrationsPerWindow = 30

// installationPattern — ключ установки, который игра генерирует сама: он же служит паролем устройства.
var installationPattern = regexp.MustCompile(`^inst_[0-9a-f]{16,64}$`)

type gameRegisterInput struct {
	Phone          string `json:"phone"`
	DisplayName    string `json:"display_name"`
	InstallationID string `json:"installation_id"`
	GameVersion    string `json:"game_version"`
	Platform       string `json:"platform"`
}

func (s *Server) gameRegister(w http.ResponseWriter, r *http.Request) {
	var in gameRegisterInput
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if !s.registerLimit.allow(clientIP(r), time.Now()) {
		writeAPIError(w, fail(http.StatusTooManyRequests, "RATE_LIMITED", "Слишком много попыток. Подождите минуту."))
		return
	}
	if strings.TrimSpace(in.Phone) == "" {
		s.gameRegisterDevice(w, r, in)
		return
	}
	phone, err := auth.NormalizePhone(in.Phone)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "PHONE_INVALID", err.Error()))
		return
	}
	ctx := r.Context()
	ip := clientIP(r)
	record := func(userID string, ok bool, reason string) {
		_ = s.store.AddLoginEvent(ctx, db.LoginEvent{UserID: userID, Identifier: phone, Success: ok, Reason: reason, Method: "phone", IP: ip, UserAgent: userAgent(r)})
	}
	if n, err := s.store.RecentLoginFailures(ctx, phone, ip, sinceWindow()); err == nil && n >= maxRegistrationsPerWindow {
		writeAPIError(w, fail(http.StatusTooManyRequests, "RATE_LIMITED", "Слишком много попыток. Подождите несколько минут."))
		return
	}

	name := cleanName(in.DisplayName)
	user, err := s.store.GetUserByPhone(ctx, phone)
	created := false
	switch {
	case errors.Is(err, sql.ErrNoRows):
		installationID, err := s.ensureInstallation(ctx, in.InstallationID, in.Platform, in.GameVersion)
		if err != nil {
			writeAPIError(w, err)
			return
		}
		if name == "" {
			name = "Игрок"
		}
		user, err = s.store.CreateUser(ctx, db.NewUser{
			Username: "player_" + strings.TrimPrefix(auth.NewID("x"), "x_")[:10], DisplayName: name, Phone: phone,
			Kind: db.KindPlayer, InstallationID: installationID, RoleSlugs: []string{auth.RolePlayer},
		})
		if err != nil {
			writeAPIError(w, err)
			return
		}
		created = true
	case err != nil:
		writeAPIError(w, err)
		return
	default:
		if user.Status != db.StatusActive {
			record(user.ID, false, "blocked")
			writeAPIError(w, fail(http.StatusForbidden, "BLOCKED", "Аккаунт заблокирован"))
			return
		}
		if user.InstallationID == "" {
			installationID, err := s.ensureInstallation(ctx, in.InstallationID, in.Platform, in.GameVersion)
			if err != nil {
				writeAPIError(w, err)
				return
			}
			_ = s.store.SetUserInstallation(ctx, user.ID, installationID)
		}
		if name != "" && name != user.DisplayName {
			_ = s.store.UpdateUserProfile(ctx, user.ID, name, nil)
		}
	}

	s.finishGameLogin(w, r, user, created, "phone", phone)
}

// gameRegisterDevice — вход без телефона: игрок — это установка игры. Новый ключ — новый игрок, знакомый — тот же.
func (s *Server) gameRegisterDevice(w http.ResponseWriter, r *http.Request, in gameRegisterInput) {
	id := strings.TrimSpace(in.InstallationID)
	if !installationPattern.MatchString(id) {
		writeAPIError(w, fail(http.StatusBadRequest, "INSTALLATION_INVALID", "Нужен номер телефона или installation_id вида inst_<16–64 hex>"))
		return
	}
	ctx := r.Context()
	user, err := s.store.GetUserByInstallation(ctx, id)
	created := false
	switch {
	case errors.Is(err, sql.ErrNoRows):
		if ok, err := s.store.InstallationExists(ctx, id); err != nil {
			writeAPIError(w, err)
			return
		} else if !ok {
			meta, _ := json.Marshal(map[string]string{"source": "game", "platform": truncate(in.Platform, 64), "game_version": truncate(in.GameVersion, 32)})
			if err := s.store.CreateInstallation(ctx, id, meta); err != nil {
				writeAPIError(w, err)
				return
			}
		}
		name := cleanName(in.DisplayName)
		if name == "" {
			name = "Игрок " + id[len(id)-4:]
		}
		user, err = s.store.CreateUser(ctx, db.NewUser{
			Username: "player_" + strings.TrimPrefix(auth.NewID("x"), "x_")[:10], DisplayName: name,
			Kind: db.KindPlayer, InstallationID: id, RoleSlugs: []string{auth.RolePlayer},
		})
		if err != nil {
			writeAPIError(w, err)
			return
		}
		created = true
	case err != nil:
		writeAPIError(w, err)
		return
	case user.Status != db.StatusActive:
		writeAPIError(w, fail(http.StatusForbidden, "BLOCKED", "Аккаунт заблокирован"))
		return
	}
	s.finishGameLogin(w, r, user, created, "device", id)
}

// finishGameLogin выдаёт игровой токен и пишет вход в журнал.
func (s *Server) finishGameLogin(w http.ResponseWriter, r *http.Request, user db.User, created bool, method, identifier string) {
	ctx := r.Context()
	token, _, err := s.openSession(ctx, r, user.ID, "game")
	if err != nil {
		writeAPIError(w, err)
		return
	}
	ip := clientIP(r)
	_ = s.store.MarkLogin(ctx, user.ID, ip)
	reason := ""
	if created {
		reason = "registered"
	}
	_ = s.store.AddLoginEvent(ctx, db.LoginEvent{UserID: user.ID, Identifier: identifier, Success: true, Reason: reason, Method: method, IP: ip, UserAgent: userAgent(r)})
	user, _ = s.store.GetUser(ctx, user.ID)
	status := http.StatusOK
	if created {
		status = http.StatusCreated
	}
	writeJSON(w, status, map[string]any{"token": token, "created": created, "user": gameUserView(user)})
}

// gameModel — доступна ли модель: для кнопки «Проверить модель» в настройках игры. Адрес провайдера не отдаём.
func (s *Server) gameModel(w http.ResponseWriter, r *http.Request, a *Actor) {
	cfg, _, err := s.effectiveProvider(r.Context())
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if strings.EqualFold(cfg.Mode, "mock") || cfg.Mode == "" {
		writeJSON(w, http.StatusOK, map[string]any{"online": true, "mode": "mock", "model": "mock", "loaded": true,
			"note": "Сервер в режиме mock: вместо нейросети отвечают заглушки"})
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 10*time.Second)
	defer cancel()
	status, err := s.llm.Status(ctx, cfg)
	out := map[string]any{"online": err == nil && status.Online, "mode": cfg.Mode, "model": status.SelectedModel, "found": status.ModelFound}
	if status.ModelLoaded != nil {
		out["loaded"] = *status.ModelLoaded
	}
	if cfg.Model == "" {
		// Модель не задана — сервер возьмёт загруженную или первую доступную.
		for _, m := range status.Models {
			if m.Loaded && !strings.Contains(strings.ToLower(m.Type+m.Key), "embed") {
				out["model"], out["loaded"], out["found"] = m.Key, true, true
				break
			}
		}
		if out["model"] == "" && len(status.Models) > 0 {
			out["model"], out["found"] = status.Models[0].Key, true
		}
	}
	if err != nil {
		out["error"] = "Сервер модели недоступен"
	}
	writeJSON(w, http.StatusOK, out)
}

// ensureInstallation — установка игры, к которой привязываются сессии игрока.
func (s *Server) ensureInstallation(ctx context.Context, id, platform, version string) (string, error) {
	if id != "" {
		if ok, err := s.store.InstallationExists(ctx, id); err != nil || ok {
			return id, err
		}
	}
	meta, _ := json.Marshal(map[string]string{"source": "game", "platform": truncate(platform, 64), "game_version": truncate(version, 32)})
	newID := newID("inst")
	return newID, s.store.CreateInstallation(ctx, newID, meta)
}

func gameUserView(u db.User) map[string]any {
	return map[string]any{
		"id": u.ID, "display_name": u.DisplayName, "phone": u.Phone, "installation_id": u.InstallationID, "created_at": u.CreatedAt,
	}
}

func (s *Server) gameMe(w http.ResponseWriter, r *http.Request, a *Actor) {
	writeJSON(w, http.StatusOK, gameUserView(a.User))
}

func (s *Server) gameUpdateMe(w http.ResponseWriter, r *http.Request, a *Actor) {
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
	user, err := s.store.GetUser(r.Context(), a.User.ID)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, gameUserView(user))
}

func (s *Server) gameLogout(w http.ResponseWriter, r *http.Request, a *Actor) {
	_ = s.store.RevokeAuthSession(r.Context(), a.User.ID, a.SessionID, "logout")
	writeJSON(w, http.StatusOK, map[string]any{"ok": true})
}

func (s *Server) gameCreateSession(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		GameVersion string          `json:"game_version"`
		Scenario    string          `json:"scenario"`
		State       json.RawMessage `json:"state"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	installationID := a.User.InstallationID
	if installationID == "" {
		var err error
		if installationID, err = s.ensureInstallation(r.Context(), "", "", in.GameVersion); err != nil {
			writeAPIError(w, err)
			return
		}
		_ = s.store.SetUserInstallation(r.Context(), a.User.ID, installationID)
	}
	if in.Scenario == "" {
		in.Scenario = "night-1"
	}
	v := db.Session{ID: newID("sess"), InstallationID: installationID, GameVersion: truncate(in.GameVersion, 32), Scenario: truncate(in.Scenario, 64), State: in.State}
	if err := s.store.CreateSession(r.Context(), v); err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusCreated, map[string]string{"session_id": v.ID})
}

// ownSession — сессия принадлежит установке игрока.
func (s *Server) ownSession(r *http.Request, a *Actor, id string) (db.Session, error) {
	sess, err := s.store.GetSession(r.Context(), id)
	if err != nil {
		return sess, err
	}
	if sess.InstallationID == "" || sess.InstallationID != a.User.InstallationID {
		return sess, fail(http.StatusNotFound, "NOT_FOUND", "Сессия не найдена")
	}
	return sess, nil
}

func (s *Server) gameAddEvent(w http.ResponseWriter, r *http.Request, a *Actor) {
	if _, err := s.ownSession(r, a, r.PathValue("id")); err != nil {
		writeAPIError(w, err)
		return
	}
	s.addEvent(w, r)
}

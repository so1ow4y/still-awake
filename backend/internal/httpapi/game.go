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

// Игровое API. Игрок обязательно заводит аккаунт: регистрация — свой логин и пароль (POST /v1/game/register),
// вход — ими же (POST /v1/game/login, player.go). Игра хранит логин и пароль в файле и входит при каждом
// запуске; в ответ — токен, дальше игра ходит с Authorization: Bearer <токен>. Анонимного входа (по ключу
// установки или номеру телефона без SMS) нет: и то и другое позволяло войти под чужим игроком.

const maxRegistrationsPerWindow = 30

// installationPattern — ключ установки, который присылает игра при регистрации: только чтобы записать платформу
// и версию. Аккаунтом он не является — войти по нему нельзя.
var installationPattern = regexp.MustCompile(`^inst_[0-9a-f]{16,64}$`)

type gameRegisterInput struct {
	Username       string `json:"username"`
	Password       string `json:"password"`
	DisplayName    string `json:"display_name"`
	InstallationID string `json:"installation_id"`
	GameVersion    string `json:"game_version"`
	Platform       string `json:"platform"`
}

// gameRegister — новый игрок со своим логином и паролем. Логин занят — 409; к аккаунту сразу привязывается
// установка (её ночи — ночи игрока), а ответ — как у входа: токен и профиль.
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
	username, err := auth.NormalizeUsername(in.Username)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "USERNAME_INVALID", err.Error()))
		return
	}
	hash, err := auth.HashPassword(in.Password)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "PASSWORD_INVALID", err.Error()))
		return
	}
	ctx := r.Context()
	// Ключ установки от игры берётся, только если он ещё ничей: иначе — новая установка.
	installationID := ""
	if id := strings.TrimSpace(in.InstallationID); installationPattern.MatchString(id) {
		if _, err := s.store.GetUserByInstallation(ctx, id); errors.Is(err, sql.ErrNoRows) {
			installationID = id
		}
	}
	installationID, err = s.ensureInstallation(ctx, installationID, in.Platform, in.GameVersion)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	name := cleanName(in.DisplayName)
	if name == "" {
		name = username
	}
	user, err := s.store.CreateUser(ctx, db.NewUser{
		Username: username, DisplayName: name, Kind: db.KindPlayer, PasswordHash: hash,
		InstallationID: installationID, RoleSlugs: []string{auth.RolePlayer},
	})
	if errors.Is(err, db.ErrConflict) {
		writeAPIError(w, fail(http.StatusConflict, "USERNAME_TAKEN", "Логин уже занят — придумайте другой"))
		return
	}
	if err != nil {
		writeAPIError(w, err)
		return
	}
	s.finishGameLogin(w, r, user, true, "game_register", username)
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

// ensureInstallation — установка игры, к которой привязываются сессии игрока. id — ключ от игры (уже проверенный):
// есть — берётся как есть, нет такого — создаётся с ним; пустой — сервер придумывает свой.
func (s *Server) ensureInstallation(ctx context.Context, id, platform, version string) (string, error) {
	if id != "" {
		if ok, err := s.store.InstallationExists(ctx, id); err != nil || ok {
			return id, err
		}
	} else {
		id = newID("inst")
	}
	meta, _ := json.Marshal(map[string]string{"source": "game", "platform": truncate(platform, 64), "game_version": truncate(version, 32)})
	return id, s.store.CreateInstallation(ctx, id, meta)
}

func gameUserView(u db.User) map[string]any {
	return map[string]any{
		"id": u.ID, "username": u.Username, "display_name": u.DisplayName, "installation_id": u.InstallationID,
		"has_password": u.HasPassword, "created_at": u.CreatedAt,
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
	if err := s.store.UpdateUserProfile(r.Context(), a.User.ID, cleanName(in.DisplayName)); err != nil {
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

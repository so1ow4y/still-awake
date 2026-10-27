package httpapi

import (
	"context"
	"net/http"
	"strconv"
	"strings"

	"still-awake-backend/internal/db"
	"still-awake-backend/internal/llm"
)

const (
	settingProvider     = "llm.provider"
	settingPromptActive = "llm.prompt.active_id"
)

// savedProvider — провайдер без ключа: ключ живёт только в .env (LLM_API_KEY) или приходит на время запроса.
type savedProvider struct {
	Mode        string  `json:"mode"`
	BaseURL     string  `json:"base_url"`
	Path        string  `json:"path"`
	Model       string  `json:"model"`
	Temperature float64 `json:"temperature"`
}

// effectiveProvider — настройки из панели поверх значений из .env.
func (s *Server) effectiveProvider(ctx context.Context) (llm.Config, bool, error) {
	cfg := s.cfg.DefaultProvider
	var saved savedProvider
	found, err := s.store.GetSetting(ctx, settingProvider, &saved)
	if err != nil || !found {
		return cfg, false, err
	}
	cfg.Mode, cfg.BaseURL, cfg.Path, cfg.Model, cfg.Temperature = saved.Mode, saved.BaseURL, saved.Path, saved.Model, saved.Temperature
	return cfg, true, nil
}

func (s *Server) getProvider(w http.ResponseWriter, r *http.Request, a *Actor) {
	cfg, saved, err := s.effectiveProvider(r.Context())
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"provider":           publicProviderConfig(cfg),
		"saved_in_panel":     saved,
		"env_default":        publicProviderConfig(s.cfg.DefaultProvider),
		"api_key_from_env":   s.cfg.DefaultProvider.APIKey != "",
		"overrides_allowed":  s.overridesAllowed(a),
		"provider_allowlist": s.cfg.ProviderAllowlist,
		"unsafe_urls":        s.cfg.AllowUnsafeProviderURLs,
	})
}

func (s *Server) putProvider(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in savedProvider
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	in.Mode = strings.TrimSpace(in.Mode)
	if in.Mode == "" {
		in.Mode = "mock"
	}
	cfg := llm.Config{Mode: in.Mode, BaseURL: strings.TrimSpace(in.BaseURL), Path: strings.TrimSpace(in.Path), Model: strings.TrimSpace(in.Model), Temperature: in.Temperature}
	if err := s.validateProvider(cfg); err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "PROVIDER_INVALID", err.Error()))
		return
	}
	if cfg.Temperature < 0 || cfg.Temperature > 2 {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Temperature — от 0 до 2"))
		return
	}
	saved := savedProvider{Mode: cfg.Mode, BaseURL: cfg.BaseURL, Path: cfg.Path, Model: cfg.Model, Temperature: cfg.Temperature}
	if err := s.store.SetSetting(r.Context(), settingProvider, saved, a.User.ID); err != nil {
		writeAPIError(w, err)
		return
	}
	s.getProvider(w, r, a)
}

// probeConfig — провайдер для проверки: из тела запроса (ключ — только на время запроса) или сохранённый.
func (s *Server) probeConfig(r *http.Request) (llm.Config, error) {
	var in llm.Config
	if err := decodeJSON(r, &in); err != nil {
		return in, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error())
	}
	if in.Mode == "" && in.BaseURL == "" {
		cfg, _, err := s.effectiveProvider(r.Context())
		return cfg, err
	}
	if in.APIKey == "" {
		in.APIKey = s.cfg.DefaultProvider.APIKey
	}
	if err := s.validateProvider(in); err != nil {
		return in, fail(http.StatusBadRequest, "PROVIDER_INVALID", err.Error())
	}
	return in, nil
}

func (s *Server) providerModels(w http.ResponseWriter, r *http.Request, a *Actor) {
	cfg, err := s.probeConfig(r)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	models, err := s.llm.ListModels(r.Context(), cfg)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadGateway, "PROVIDER_UNAVAILABLE", err.Error()))
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"models": models})
}

func (s *Server) providerStatus(w http.ResponseWriter, r *http.Request, a *Actor) {
	cfg, err := s.probeConfig(r)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	status, err := s.llm.Status(r.Context(), cfg)
	if err != nil {
		writeJSON(w, http.StatusOK, map[string]any{"online": false, "error": err.Error(), "base_url": cfg.BaseURL, "selected_model": cfg.Model})
		return
	}
	writeJSON(w, http.StatusOK, status)
}

// ------------------------------------------------------------------------- промпт

type promptView struct {
	Active      *db.PromptVersion `json:"active"`
	DefaultText string            `json:"default_text"`
	Text        string            `json:"text"`
	// Guard — правила сервера, которые всегда идут после промпта (только для просмотра).
	Guard string `json:"guard"`
}

func (s *Server) activePrompt(ctx context.Context) (promptView, error) {
	view := promptView{DefaultText: DefaultSystemPrompt, Text: DefaultSystemPrompt, Guard: guardPrompt}
	var id int64
	found, err := s.store.GetSetting(ctx, settingPromptActive, &id)
	if err != nil || !found || id == 0 {
		return view, err
	}
	v, err := s.store.GetPromptVersion(ctx, id)
	if err != nil {
		// Версию удалили руками через SQL — откатываемся на промпт по умолчанию.
		return view, nil
	}
	view.Active, view.Text = &v, v.Text
	return view, nil
}

func (s *Server) getPrompt(w http.ResponseWriter, r *http.Request, a *Actor) {
	view, err := s.activePrompt(r.Context())
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, view)
}

func (s *Server) listPromptVersions(w http.ResponseWriter, r *http.Request, a *Actor) {
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListPromptVersions(r.Context(), limit)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

// savePrompt — новая версия сразу становится активной; старые остаются в истории.
func (s *Server) savePrompt(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		Text    string `json:"text"`
		Comment string `json:"comment"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	text := strings.TrimSpace(in.Text)
	if text == "" {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Промпт пустой"))
		return
	}
	if len(text) > 32_000 {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Промпт длиннее 32 000 символов"))
		return
	}
	v, err := s.store.AddPromptVersion(r.Context(), db.PromptVersion{Text: text, Comment: truncate(strings.TrimSpace(in.Comment), 300), AuthorID: a.User.ID, AuthorName: a.User.Username})
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if err := s.store.SetSetting(r.Context(), settingPromptActive, v.ID, a.User.ID); err != nil {
		writeAPIError(w, err)
		return
	}
	s.getPrompt(w, r, a)
}

func (s *Server) activatePromptVersion(w http.ResponseWriter, r *http.Request, a *Actor) {
	id, err := pathInt64(r)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Неверный номер версии"))
		return
	}
	if id != 0 {
		if _, err := s.store.GetPromptVersion(r.Context(), id); err != nil {
			writeAPIError(w, err)
			return
		}
	}
	if err := s.store.SetSetting(r.Context(), settingPromptActive, id, a.User.ID); err != nil {
		writeAPIError(w, err)
		return
	}
	s.getPrompt(w, r, a)
}

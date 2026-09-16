package httpapi

import (
	"database/sql"
	"errors"
	"net/http"
	"strconv"

	"still-awake-backend/internal/db"
	"still-awake-backend/internal/llm"
)

func (s *Server) debugProviderModels(w http.ResponseWriter, r *http.Request) {
	if !s.cfg.AllowClientProviderOverrides {
		writeError(w, http.StatusForbidden, errors.New("client provider overrides are disabled"))
		return
	}
	var cfg llm.Config
	if err := decodeJSON(r, &cfg); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if err := s.validateProvider(cfg); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	models, err := s.llm.ListModels(r.Context(), cfg)
	if err != nil {
		writeError(w, http.StatusBadGateway, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"models": models})
}

func (s *Server) debugProviderStatus(w http.ResponseWriter, r *http.Request) {
	if !s.cfg.AllowClientProviderOverrides {
		writeError(w, http.StatusForbidden, errors.New("client provider overrides are disabled"))
		return
	}
	var cfg llm.Config
	if err := decodeJSON(r, &cfg); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if err := s.validateProvider(cfg); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	status, err := s.llm.Status(r.Context(), cfg)
	if err != nil {
		writeJSON(w, http.StatusBadGateway, map[string]any{"online": false, "error": err.Error(), "base_url": cfg.BaseURL, "selected_model": cfg.Model})
		return
	}
	writeJSON(w, http.StatusOK, status)
}

func (s *Server) adminListLLMRequests(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListLLMRequestsFiltered(r.Context(), db.LLMRequestFilter{
		SessionID: r.URL.Query().Get("session_id"), InstallationID: r.URL.Query().Get("installation_id"),
		Query: r.URL.Query().Get("q"), Limit: limit,
	})
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminListSessionLLMRequests(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListLLMRequestsFiltered(r.Context(), db.LLMRequestFilter{SessionID: r.PathValue("id"), Query: r.URL.Query().Get("q"), Limit: limit})
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminDeleteLLMRequest(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	id, err := pathInt64(r)
	if err != nil {
		writeError(w, 400, errors.New("invalid llm request id"))
		return
	}
	if err := s.store.DeleteLLMRequest(r.Context(), id); err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			writeError(w, 404, errors.New("llm request not found"))
			return
		}
		writeError(w, 500, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) adminDBSchema(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	rows, err := s.store.Schema(r.Context())
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, map[string]any{
		"objects":            rows,
		"sql_writes_enabled": s.cfg.AdminAllowSQLWrites,
	})
}

func (s *Server) adminDBQuery(w http.ResponseWriter, r *http.Request) {
	if !s.requireAdmin(w, r) {
		return
	}
	var in struct {
		SQL        string `json:"sql"`
		AllowWrite bool   `json:"allow_write"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, 400, err)
		return
	}
	if in.AllowWrite && !s.cfg.AdminAllowSQLWrites {
		writeError(w, 403, errors.New("write SQL is disabled by ADMIN_ALLOW_SQL_WRITES"))
		return
	}
	result, err := s.store.AdminSQL(r.Context(), in.SQL, in.AllowWrite && s.cfg.AdminAllowSQLWrites)
	if err != nil {
		writeError(w, 400, err)
		return
	}
	// Make sure an empty query result still serializes as [] instead of null.
	if result.Kind == "query" && result.Rows == nil {
		result.Rows = []map[string]any{}
	}
	writeJSON(w, 200, result)
}

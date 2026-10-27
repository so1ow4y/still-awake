package httpapi

import (
	"database/sql"
	"errors"
	"net/http"
	"strconv"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

func (s *Server) adminListLLMRequests(w http.ResponseWriter, r *http.Request) {
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
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	rows, err := s.store.ListLLMRequestsFiltered(r.Context(), db.LLMRequestFilter{SessionID: r.PathValue("id"), Query: r.URL.Query().Get("q"), Limit: limit})
	if err != nil {
		writeError(w, 500, err)
		return
	}
	writeJSON(w, 200, rows)
}

func (s *Server) adminDeleteLLMRequest(w http.ResponseWriter, r *http.Request) {
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

// adminDBQuery: чтение — с правом db.read; изменяющий SQL — ещё и db.write, и ADMIN_ALLOW_SQL_WRITES=true.
func (s *Server) adminDBQuery(w http.ResponseWriter, r *http.Request, a *Actor) {
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
	if in.AllowWrite && !a.Can(auth.PermDBWrite) {
		writeAPIError(w, fail(http.StatusForbidden, "FORBIDDEN", "Для изменяющего SQL нужно право db.write"))
		return
	}
	result, err := s.store.AdminSQL(r.Context(), in.SQL, in.AllowWrite)
	if err != nil {
		writeError(w, 400, err)
		return
	}
	// Пустой результат — [], а не null.
	if result.Kind == "query" && result.Rows == nil {
		result.Rows = []map[string]any{}
	}
	writeJSON(w, 200, result)
}

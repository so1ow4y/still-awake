package httpapi

import (
	"errors"
	"net/http"
	"strconv"
)

// analyticsSummary — доступ по праву analytics.read (роль «Аналитик» и выше), а не по отдельному токену.
func (s *Server) analyticsSummary(w http.ResponseWriter, r *http.Request) {
	days, _ := strconv.Atoi(r.URL.Query().Get("days"))
	if days < 0 || days > 3650 {
		writeError(w, http.StatusBadRequest, errors.New("days must be between 0 and 3650"))
		return
	}
	v, err := s.store.Analytics(r.Context(), days)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}
	writeJSON(w, http.StatusOK, v)
}

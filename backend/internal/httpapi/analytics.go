package httpapi

import (
	"errors"
	"net/http"
	"strconv"
)

func (s *Server) analyticsOK(r *http.Request) bool {
	return s.cfg.AnalyticsToken != "" && r.Header.Get("X-Analytics-Token") == s.cfg.AnalyticsToken
}

func (s *Server) requireAnalytics(w http.ResponseWriter, r *http.Request) bool {
	if !s.analyticsOK(r) {
		writeError(w, http.StatusUnauthorized, errors.New("analytics unauthorized"))
		return false
	}
	return true
}

func (s *Server) analyticsSummary(w http.ResponseWriter, r *http.Request) {
	if !s.requireAnalytics(w, r) {
		return
	}
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

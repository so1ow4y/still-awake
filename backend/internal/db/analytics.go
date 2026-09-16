package db

import (
	"context"
	"fmt"
)

type AnalyticsSummary struct {
	PeriodDays          int              `json:"period_days"`
	Requests            int64            `json:"requests"`
	Success             int64            `json:"success"`
	Errors              int64            `json:"errors"`
	ActiveInstallations int64            `json:"active_installations"`
	ActiveSessions      int64            `json:"active_sessions"`
	PromptTokens        int64            `json:"prompt_tokens"`
	CompletionTokens    int64            `json:"completion_tokens"`
	TotalTokens         int64            `json:"total_tokens"`
	AvgLatencyMS        float64          `json:"avg_latency_ms"`
	AvgTokensPerSecond  float64          `json:"avg_tokens_per_second"`
	AvgTTFTMS           float64          `json:"avg_time_to_first_token_ms"`
	Daily               []AnalyticsDaily `json:"daily"`
	Models              []AnalyticsModel `json:"models"`
	Users               []AnalyticsUser  `json:"users"`
}

type AnalyticsDaily struct {
	Day              string `json:"day"`
	Requests         int64  `json:"requests"`
	Errors           int64  `json:"errors"`
	PromptTokens     int64  `json:"prompt_tokens"`
	CompletionTokens int64  `json:"completion_tokens"`
	TotalTokens      int64  `json:"total_tokens"`
}

type AnalyticsModel struct {
	Model           string  `json:"model"`
	Provider        string  `json:"provider"`
	Requests        int64   `json:"requests"`
	Errors          int64   `json:"errors"`
	TotalTokens     int64   `json:"total_tokens"`
	AvgLatencyMS    float64 `json:"avg_latency_ms"`
	AvgTokensPerSec float64 `json:"avg_tokens_per_second"`
}

type AnalyticsUser struct {
	InstallationID string  `json:"installation_id"`
	Requests       int64   `json:"requests"`
	Sessions       int64   `json:"sessions"`
	Errors         int64   `json:"errors"`
	TotalTokens    int64   `json:"total_tokens"`
	AvgLatencyMS   float64 `json:"avg_latency_ms"`
	LastRequestAt  string  `json:"last_request_at"`
}

func analyticsWhere(days int) (string, []any) {
	if days <= 0 {
		return "", nil
	}
	return ` WHERE julianday(created_at) >= julianday('now', ?)`, []any{fmt.Sprintf("-%d days", days)}
}

func (s *Store) Analytics(ctx context.Context, days int) (AnalyticsSummary, error) {
	var out AnalyticsSummary
	out.PeriodDays = days
	where, args := analyticsWhere(days)

	q := `SELECT
	COUNT(*),
	COALESCE(SUM(CASE WHEN status='ok' THEN 1 ELSE 0 END),0),
	COALESCE(SUM(CASE WHEN status!='ok' THEN 1 ELSE 0 END),0),
	COUNT(DISTINCT CASE WHEN installation_id!='' THEN installation_id END),
	COUNT(DISTINCT CASE WHEN session_id!='' THEN session_id END),
	COALESCE(SUM(prompt_tokens),0), COALESCE(SUM(completion_tokens),0), COALESCE(SUM(total_tokens),0),
	COALESCE(AVG(latency_ms),0),
	COALESCE(AVG(CASE WHEN tokens_per_second>0 THEN tokens_per_second END),0),
	COALESCE(AVG(CASE WHEN time_to_first_token_ms>0 THEN time_to_first_token_ms END),0)
	FROM llm_requests` + where
	if err := s.DB.QueryRowContext(ctx, q, args...).Scan(
		&out.Requests, &out.Success, &out.Errors, &out.ActiveInstallations, &out.ActiveSessions,
		&out.PromptTokens, &out.CompletionTokens, &out.TotalTokens,
		&out.AvgLatencyMS, &out.AvgTokensPerSecond, &out.AvgTTFTMS,
	); err != nil {
		return out, err
	}

	dailyQ := `SELECT substr(created_at,1,10), COUNT(*),
	COALESCE(SUM(CASE WHEN status!='ok' THEN 1 ELSE 0 END),0),
	COALESCE(SUM(prompt_tokens),0), COALESCE(SUM(completion_tokens),0), COALESCE(SUM(total_tokens),0)
	FROM llm_requests` + where + ` GROUP BY substr(created_at,1,10) ORDER BY substr(created_at,1,10)`
	rows, err := s.DB.QueryContext(ctx, dailyQ, args...)
	if err != nil {
		return out, err
	}
	for rows.Next() {
		var v AnalyticsDaily
		if err := rows.Scan(&v.Day, &v.Requests, &v.Errors, &v.PromptTokens, &v.CompletionTokens, &v.TotalTokens); err != nil {
			rows.Close()
			return out, err
		}
		out.Daily = append(out.Daily, v)
	}
	if err := rows.Close(); err != nil {
		return out, err
	}

	modelQ := `SELECT COALESCE(NULLIF(model,''),'unknown'), COALESCE(NULLIF(provider,''),'unknown'), COUNT(*),
	COALESCE(SUM(CASE WHEN status!='ok' THEN 1 ELSE 0 END),0), COALESCE(SUM(total_tokens),0),
	COALESCE(AVG(latency_ms),0), COALESCE(AVG(CASE WHEN tokens_per_second>0 THEN tokens_per_second END),0)
	FROM llm_requests` + where + ` GROUP BY model, provider ORDER BY COUNT(*) DESC LIMIT 30`
	rows, err = s.DB.QueryContext(ctx, modelQ, args...)
	if err != nil {
		return out, err
	}
	for rows.Next() {
		var v AnalyticsModel
		if err := rows.Scan(&v.Model, &v.Provider, &v.Requests, &v.Errors, &v.TotalTokens, &v.AvgLatencyMS, &v.AvgTokensPerSec); err != nil {
			rows.Close()
			return out, err
		}
		out.Models = append(out.Models, v)
	}
	if err := rows.Close(); err != nil {
		return out, err
	}

	userQ := `SELECT installation_id, COUNT(*), COUNT(DISTINCT session_id),
	COALESCE(SUM(CASE WHEN status!='ok' THEN 1 ELSE 0 END),0), COALESCE(SUM(total_tokens),0),
	COALESCE(AVG(latency_ms),0), MAX(created_at)
	FROM llm_requests` + where
	if where == "" {
		userQ += ` WHERE installation_id!=''`
	} else {
		userQ += ` AND installation_id!=''`
	}
	userQ += ` GROUP BY installation_id ORDER BY COUNT(*) DESC LIMIT 50`
	rows, err = s.DB.QueryContext(ctx, userQ, args...)
	if err != nil {
		return out, err
	}
	for rows.Next() {
		var v AnalyticsUser
		if err := rows.Scan(&v.InstallationID, &v.Requests, &v.Sessions, &v.Errors, &v.TotalTokens, &v.AvgLatencyMS, &v.LastRequestAt); err != nil {
			rows.Close()
			return out, err
		}
		out.Users = append(out.Users, v)
	}
	if err := rows.Close(); err != nil {
		return out, err
	}
	if out.Daily == nil {
		out.Daily = []AnalyticsDaily{}
	}
	if out.Models == nil {
		out.Models = []AnalyticsModel{}
	}
	if out.Users == nil {
		out.Users = []AnalyticsUser{}
	}
	return out, nil
}

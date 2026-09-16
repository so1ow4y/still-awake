package db

import (
	"context"
	"database/sql"
	"encoding/json"
	"strings"
)

type LLMRequest struct {
	ID                 int64           `json:"id"`
	InstallationID     string          `json:"installation_id"`
	SessionID          string          `json:"session_id"`
	RequestMode        string          `json:"request_mode"`
	Provider           string          `json:"provider"`
	Model              string          `json:"model"`
	Endpoint           string          `json:"endpoint"`
	PromptTokens       int             `json:"prompt_tokens"`
	CompletionTokens   int             `json:"completion_tokens"`
	TotalTokens        int             `json:"total_tokens"`
	LatencyMS          int64           `json:"latency_ms"`
	TokensPerSecond    float64         `json:"tokens_per_second"`
	TimeToFirstTokenMS int64           `json:"time_to_first_token_ms"`
	Status             string          `json:"status"`
	ErrorText          string          `json:"error_text,omitempty"`
	UserMessageID      int64           `json:"user_message_id"`
	AssistantMessageID int64           `json:"assistant_message_id"`
	UserMessage        string          `json:"user_message"`
	AssistantMessage   string          `json:"assistant_message"`
	RequestMessages    json.RawMessage `json:"request_messages"`
	HistoryCount       int             `json:"history_count"`
	CreatedAt          string          `json:"created_at"`
}

type LLMRequestFilter struct {
	SessionID      string
	InstallationID string
	Query          string
	Limit          int
}

func (s *Store) AddLLMRequest(ctx context.Context, v LLMRequest) error {
	requestMessages := v.RequestMessages
	if len(requestMessages) == 0 {
		requestMessages = json.RawMessage(`[]`)
	}
	_, err := s.DB.ExecContext(ctx, `
INSERT INTO llm_requests(
  installation_id, session_id, request_mode, provider, model, endpoint,
  prompt_tokens, completion_tokens, total_tokens, latency_ms,
  tokens_per_second, time_to_first_token_ms, status, error_text,
  user_message_id, assistant_message_id, user_message, assistant_message,
  request_messages_json, history_count, created_at
) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)`,
		v.InstallationID, v.SessionID, v.RequestMode, v.Provider, v.Model, v.Endpoint,
		v.PromptTokens, v.CompletionTokens, v.TotalTokens, v.LatencyMS,
		v.TokensPerSecond, v.TimeToFirstTokenMS, v.Status, v.ErrorText,
		v.UserMessageID, v.AssistantMessageID, v.UserMessage, v.AssistantMessage,
		string(requestMessages), v.HistoryCount, now())
	return err
}

func (s *Store) ListLLMRequests(ctx context.Context, sessionID string, limit int) ([]LLMRequest, error) {
	return s.ListLLMRequestsFiltered(ctx, LLMRequestFilter{SessionID: sessionID, Limit: limit})
}

func (s *Store) ListLLMRequestsFiltered(ctx context.Context, f LLMRequestFilter) ([]LLMRequest, error) {
	if f.Limit <= 0 || f.Limit > 1000 {
		f.Limit = 200
	}
	q := `SELECT id, installation_id, session_id, request_mode, provider, model, endpoint,
                 prompt_tokens, completion_tokens, total_tokens, latency_ms,
                 tokens_per_second, time_to_first_token_ms, status, error_text,
                 user_message_id, assistant_message_id, user_message, assistant_message,
                 request_messages_json, history_count, created_at
          FROM llm_requests`
	var where []string
	var args []any
	if f.SessionID != "" {
		where = append(where, `session_id=?`)
		args = append(args, f.SessionID)
	}
	if f.InstallationID != "" {
		where = append(where, `installation_id=?`)
		args = append(args, f.InstallationID)
	}
	if strings.TrimSpace(f.Query) != "" {
		like := "%" + strings.TrimSpace(f.Query) + "%"
		where = append(where, `(installation_id LIKE ? OR session_id LIKE ? OR model LIKE ? OR user_message LIKE ? OR assistant_message LIKE ?)`)
		args = append(args, like, like, like, like, like)
	}
	if len(where) > 0 {
		q += ` WHERE ` + strings.Join(where, " AND ")
	}
	q += ` ORDER BY id DESC LIMIT ?`
	args = append(args, f.Limit)
	rows, err := s.DB.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []LLMRequest
	for rows.Next() {
		var v LLMRequest
		var requestMessages string
		if err := rows.Scan(
			&v.ID, &v.InstallationID, &v.SessionID, &v.RequestMode, &v.Provider, &v.Model, &v.Endpoint,
			&v.PromptTokens, &v.CompletionTokens, &v.TotalTokens, &v.LatencyMS,
			&v.TokensPerSecond, &v.TimeToFirstTokenMS, &v.Status, &v.ErrorText,
			&v.UserMessageID, &v.AssistantMessageID, &v.UserMessage, &v.AssistantMessage,
			&requestMessages, &v.HistoryCount, &v.CreatedAt,
		); err != nil {
			return nil, err
		}
		v.RequestMessages = json.RawMessage(requestMessages)
		out = append(out, v)
	}
	return out, rows.Err()
}

func (s *Store) DeleteLLMRequest(ctx context.Context, id int64) error {
	res, err := s.DB.ExecContext(ctx, `DELETE FROM llm_requests WHERE id=?`, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

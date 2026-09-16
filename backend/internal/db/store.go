package db

import (
	"context"
	"database/sql"
	_ "embed"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"

	_ "modernc.org/sqlite"
)

type Store struct{ DB *sql.DB }

//go:embed schema.sql
var schemaSQL string

type Session struct {
	ID             string          `json:"id"`
	InstallationID string          `json:"installation_id"`
	GameVersion    string          `json:"game_version"`
	Scenario       string          `json:"scenario"`
	State          json.RawMessage `json:"state"`
	CreatedAt      string          `json:"created_at"`
	UpdatedAt      string          `json:"updated_at"`
}

type Message struct {
	ID        int64  `json:"id"`
	SessionID string `json:"session_id"`
	Role      string `json:"role"`
	Content   string `json:"content"`
	Provider  string `json:"provider,omitempty"`
	Model     string `json:"model,omitempty"`
	CreatedAt string `json:"created_at"`
}

type Installation struct {
	ID        string          `json:"id"`
	Metadata  json.RawMessage `json:"metadata"`
	CreatedAt string          `json:"created_at"`
}

type Event struct {
	ID        int64           `json:"id"`
	SessionID string          `json:"session_id"`
	ClientSeq int64           `json:"client_seq"`
	Type      string          `json:"type"`
	Payload   json.RawMessage `json:"payload"`
	CreatedAt string          `json:"created_at"`
}

type Overview struct {
	Installations int64 `json:"installations"`
	Sessions      int64 `json:"sessions"`
	Messages      int64 `json:"messages"`
	Events        int64 `json:"events"`
	LLMRequests   int64 `json:"llm_requests"`
}

func Open(path string) (*Store, error) {
	if path == "" {
		path = "./data/still-awake.db"
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return nil, err
	}
	dsn := fmt.Sprintf("file:%s?_pragma=busy_timeout(5000)&_pragma=journal_mode(WAL)&_pragma=foreign_keys(ON)", filepath.ToSlash(path))
	conn, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, err
	}
	conn.SetMaxOpenConns(1)
	s := &Store{DB: conn}
	if err := s.migrate(context.Background()); err != nil {
		conn.Close()
		return nil, err
	}
	return s, nil
}

func (s *Store) Close() error { return s.DB.Close() }

func (s *Store) migrate(ctx context.Context) error {
	if _, err := s.DB.ExecContext(ctx, schemaSQL); err != nil {
		return err
	}
	// v4 adds request/response audit fields to llm_requests. Existing SQLite files
	// need ALTER TABLE because CREATE TABLE IF NOT EXISTS does not modify columns.
	columns := []struct {
		name string
		ddl  string
	}{
		{"user_message_id", "INTEGER NOT NULL DEFAULT 0"},
		{"assistant_message_id", "INTEGER NOT NULL DEFAULT 0"},
		{"user_message", "TEXT NOT NULL DEFAULT ''"},
		{"assistant_message", "TEXT NOT NULL DEFAULT ''"},
		{"request_messages_json", "TEXT NOT NULL DEFAULT '[]'"},
		{"history_count", "INTEGER NOT NULL DEFAULT 0"},
	}
	for _, c := range columns {
		has, err := s.hasColumn(ctx, "llm_requests", c.name)
		if err != nil {
			return err
		}
		if !has {
			if _, err := s.DB.ExecContext(ctx, fmt.Sprintf("ALTER TABLE llm_requests ADD COLUMN %s %s", c.name, c.ddl)); err != nil {
				return err
			}
		}
	}
	_, err := s.DB.ExecContext(ctx, `
CREATE INDEX IF NOT EXISTS idx_sessions_installation_id ON sessions(installation_id, updated_at);
CREATE INDEX IF NOT EXISTS idx_llm_requests_status_created_at ON llm_requests(status, created_at);`)
	return err
}

func (s *Store) hasColumn(ctx context.Context, table, column string) (bool, error) {
	rows, err := s.DB.QueryContext(ctx, "PRAGMA table_info("+table+")")
	if err != nil {
		return false, err
	}
	defer rows.Close()
	for rows.Next() {
		var cid int
		var name, typ string
		var notnull, pk int
		var defaultValue any
		if err := rows.Scan(&cid, &name, &typ, &notnull, &defaultValue, &pk); err != nil {
			return false, err
		}
		if name == column {
			return true, nil
		}
	}
	return false, rows.Err()
}

func now() string { return time.Now().UTC().Format(time.RFC3339Nano) }

func (s *Store) CreateInstallation(ctx context.Context, id string, metadata json.RawMessage) error {
	if len(metadata) == 0 {
		metadata = json.RawMessage(`{}`)
	}
	_, err := s.DB.ExecContext(ctx, `INSERT INTO installations(id, metadata_json, created_at) VALUES(?,?,?)`, id, string(metadata), now())
	return err
}

func (s *Store) CreateSession(ctx context.Context, v Session) error {
	if len(v.State) == 0 {
		v.State = json.RawMessage(`{}`)
	}
	ts := now()
	_, err := s.DB.ExecContext(ctx, `INSERT INTO sessions(id, installation_id, game_version, scenario, state_json, created_at, updated_at) VALUES(?,?,?,?,?,?,?)`,
		v.ID, v.InstallationID, v.GameVersion, v.Scenario, string(v.State), ts, ts)
	return err
}

func (s *Store) GetSession(ctx context.Context, id string) (Session, error) {
	var out Session
	var state string
	err := s.DB.QueryRowContext(ctx, `SELECT id, installation_id, game_version, scenario, state_json, created_at, updated_at FROM sessions WHERE id=?`, id).
		Scan(&out.ID, &out.InstallationID, &out.GameVersion, &out.Scenario, &state, &out.CreatedAt, &out.UpdatedAt)
	if err != nil {
		return out, err
	}
	out.State = json.RawMessage(state)
	return out, nil
}

func (s *Store) UpdateSessionState(ctx context.Context, id string, state json.RawMessage) error {
	if len(state) == 0 {
		return nil
	}
	res, err := s.DB.ExecContext(ctx, `UPDATE sessions SET state_json=?, updated_at=? WHERE id=?`, string(state), now(), id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) AddMessage(ctx context.Context, m Message) error {
	_, err := s.AddMessageReturningID(ctx, m)
	return err
}

func (s *Store) AddMessageReturningID(ctx context.Context, m Message) (int64, error) {
	res, err := s.DB.ExecContext(ctx, `INSERT INTO messages(session_id, role, content, provider, model, created_at) VALUES(?,?,?,?,?,?)`,
		m.SessionID, m.Role, m.Content, m.Provider, m.Model, now())
	if err != nil {
		return 0, err
	}
	return res.LastInsertId()
}

func (s *Store) ListMessages(ctx context.Context, sessionID string, limit int) ([]Message, error) {
	if limit <= 0 || limit > 200 {
		limit = 50
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT id, session_id, role, content, provider, model, created_at FROM messages WHERE session_id=? ORDER BY id DESC LIMIT ?`, sessionID, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var rev []Message
	for rows.Next() {
		var m Message
		if err := rows.Scan(&m.ID, &m.SessionID, &m.Role, &m.Content, &m.Provider, &m.Model, &m.CreatedAt); err != nil {
			return nil, err
		}
		rev = append(rev, m)
	}
	out := make([]Message, len(rev))
	for i := range rev {
		out[len(rev)-1-i] = rev[i]
	}
	return out, rows.Err()
}

func (s *Store) AddEvent(ctx context.Context, sessionID string, seq int64, eventType string, payload json.RawMessage) error {
	if len(payload) == 0 {
		payload = json.RawMessage(`{}`)
	}
	_, err := s.DB.ExecContext(ctx, `INSERT INTO events(session_id, client_seq, event_type, payload_json, created_at) VALUES(?,?,?,?,?)`,
		sessionID, seq, eventType, string(payload), now())
	return err
}

func (s *Store) ListSessions(ctx context.Context, limit int) ([]Session, error) {
	return s.SearchSessions(ctx, "", "", limit)
}

func (s *Store) SearchSessions(ctx context.Context, query, installationID string, limit int) ([]Session, error) {
	if limit <= 0 || limit > 500 {
		limit = 100
	}
	q := `SELECT id, installation_id, game_version, scenario, state_json, created_at, updated_at FROM sessions`
	var where []string
	var args []any
	if installationID != "" {
		where = append(where, `installation_id=?`)
		args = append(args, installationID)
	}
	if strings.TrimSpace(query) != "" {
		like := "%" + strings.TrimSpace(query) + "%"
		where = append(where, `(id LIKE ? OR installation_id LIKE ? OR scenario LIKE ? OR game_version LIKE ?)`)
		args = append(args, like, like, like, like)
	}
	if len(where) > 0 {
		q += ` WHERE ` + strings.Join(where, " AND ")
	}
	q += ` ORDER BY updated_at DESC LIMIT ?`
	args = append(args, limit)
	rows, err := s.DB.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []Session
	for rows.Next() {
		var v Session
		var state string
		if err := rows.Scan(&v.ID, &v.InstallationID, &v.GameVersion, &v.Scenario, &state, &v.CreatedAt, &v.UpdatedAt); err != nil {
			return nil, err
		}
		v.State = json.RawMessage(state)
		out = append(out, v)
	}
	return out, rows.Err()
}

func (s *Store) DeleteSession(ctx context.Context, id string) error {
	res, err := s.DB.ExecContext(ctx, `DELETE FROM sessions WHERE id=?`, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return errors.New("session not found")
	}
	return nil
}

func (s *Store) Overview(ctx context.Context) (Overview, error) {
	var out Overview
	for table, dst := range map[string]*int64{
		"installations": &out.Installations,
		"sessions":      &out.Sessions,
		"messages":      &out.Messages,
		"events":        &out.Events,
		"llm_requests":  &out.LLMRequests,
	} {
		if err := s.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM `+table).Scan(dst); err != nil {
			return out, err
		}
	}
	return out, nil
}

func (s *Store) ListInstallations(ctx context.Context, limit int) ([]Installation, error) {
	return s.SearchInstallations(ctx, "", limit)
}

func (s *Store) SearchInstallations(ctx context.Context, query string, limit int) ([]Installation, error) {
	if limit <= 0 || limit > 500 {
		limit = 100
	}
	q := `SELECT id, metadata_json, created_at FROM installations`
	var args []any
	if strings.TrimSpace(query) != "" {
		like := "%" + strings.TrimSpace(query) + "%"
		q += ` WHERE id LIKE ? OR metadata_json LIKE ?`
		args = append(args, like, like)
	}
	q += ` ORDER BY created_at DESC LIMIT ?`
	args = append(args, limit)
	rows, err := s.DB.QueryContext(ctx, q, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []Installation
	for rows.Next() {
		var v Installation
		var meta string
		if err := rows.Scan(&v.ID, &meta, &v.CreatedAt); err != nil {
			return nil, err
		}
		v.Metadata = json.RawMessage(meta)
		out = append(out, v)
	}
	return out, rows.Err()
}

func (s *Store) UpdateInstallation(ctx context.Context, id string, metadata json.RawMessage) error {
	if len(metadata) == 0 {
		metadata = json.RawMessage(`{}`)
	}
	res, err := s.DB.ExecContext(ctx, `UPDATE installations SET metadata_json=? WHERE id=?`, string(metadata), id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) DeleteInstallation(ctx context.Context, id string) error {
	res, err := s.DB.ExecContext(ctx, `DELETE FROM installations WHERE id=?`, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) UpdateSession(ctx context.Context, id, gameVersion, scenario string, state json.RawMessage) error {
	if len(state) == 0 {
		state = json.RawMessage(`{}`)
	}
	res, err := s.DB.ExecContext(ctx, `UPDATE sessions SET game_version=?, scenario=?, state_json=?, updated_at=? WHERE id=?`, gameVersion, scenario, string(state), now(), id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) ListEvents(ctx context.Context, sessionID string, limit int) ([]Event, error) {
	if limit <= 0 || limit > 500 {
		limit = 100
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT id, session_id, client_seq, event_type, payload_json, created_at FROM events WHERE session_id=? ORDER BY id ASC LIMIT ?`, sessionID, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []Event
	for rows.Next() {
		var v Event
		var payload string
		if err := rows.Scan(&v.ID, &v.SessionID, &v.ClientSeq, &v.Type, &payload, &v.CreatedAt); err != nil {
			return nil, err
		}
		v.Payload = json.RawMessage(payload)
		out = append(out, v)
	}
	return out, rows.Err()
}

func (s *Store) UpdateMessage(ctx context.Context, id int64, role, content, provider, model string) error {
	res, err := s.DB.ExecContext(ctx, `UPDATE messages SET role=?, content=?, provider=?, model=? WHERE id=?`, role, content, provider, model, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) DeleteMessage(ctx context.Context, id int64) error {
	res, err := s.DB.ExecContext(ctx, `DELETE FROM messages WHERE id=?`, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) UpdateEvent(ctx context.Context, id, clientSeq int64, eventType string, payload json.RawMessage) error {
	if len(payload) == 0 {
		payload = json.RawMessage(`{}`)
	}
	res, err := s.DB.ExecContext(ctx, `UPDATE events SET client_seq=?, event_type=?, payload_json=? WHERE id=?`, clientSeq, eventType, string(payload), id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

func (s *Store) DeleteEvent(ctx context.Context, id int64) error {
	res, err := s.DB.ExecContext(ctx, `DELETE FROM events WHERE id=?`, id)
	if err != nil {
		return err
	}
	n, _ := res.RowsAffected()
	if n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

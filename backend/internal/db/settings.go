package db

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
)

// GetSetting читает настройку в dst. found=false — настройки ещё нет, dst не тронут.
func (s *Store) GetSetting(ctx context.Context, key string, dst any) (bool, error) {
	var raw string
	err := s.DB.QueryRowContext(ctx, `SELECT value_json FROM settings WHERE key=?`, key).Scan(&raw)
	if errors.Is(err, sql.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	return true, json.Unmarshal([]byte(raw), dst)
}

func (s *Store) SetSetting(ctx context.Context, key string, value any, updatedBy string) error {
	b, err := json.Marshal(value)
	if err != nil {
		return err
	}
	_, err = s.DB.ExecContext(ctx, `INSERT INTO settings(key, value_json, updated_by, updated_at) VALUES(?,?,?,?)
		ON CONFLICT(key) DO UPDATE SET value_json=excluded.value_json, updated_by=excluded.updated_by, updated_at=excluded.updated_at`,
		key, string(b), updatedBy, now())
	return err
}

type PromptVersion struct {
	ID         int64  `json:"id"`
	Text       string `json:"text"`
	Comment    string `json:"comment"`
	AuthorID   string `json:"author_id"`
	AuthorName string `json:"author_name"`
	CreatedAt  string `json:"created_at"`
}

func (s *Store) AddPromptVersion(ctx context.Context, v PromptVersion) (PromptVersion, error) {
	res, err := s.DB.ExecContext(ctx, `INSERT INTO prompt_versions(text, comment, author_id, author_name, created_at) VALUES(?,?,?,?,?)`,
		v.Text, v.Comment, v.AuthorID, v.AuthorName, now())
	if err != nil {
		return v, err
	}
	id, err := res.LastInsertId()
	if err != nil {
		return v, err
	}
	return s.GetPromptVersion(ctx, id)
}

func (s *Store) GetPromptVersion(ctx context.Context, id int64) (PromptVersion, error) {
	var v PromptVersion
	err := s.DB.QueryRowContext(ctx, `SELECT id, text, comment, author_id, author_name, created_at FROM prompt_versions WHERE id=?`, id).
		Scan(&v.ID, &v.Text, &v.Comment, &v.AuthorID, &v.AuthorName, &v.CreatedAt)
	return v, err
}

func (s *Store) ListPromptVersions(ctx context.Context, limit int) ([]PromptVersion, error) {
	if limit <= 0 || limit > 500 {
		limit = 100
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT id, text, comment, author_id, author_name, created_at FROM prompt_versions ORDER BY id DESC LIMIT ?`, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []PromptVersion{}
	for rows.Next() {
		var v PromptVersion
		if err := rows.Scan(&v.ID, &v.Text, &v.Comment, &v.AuthorID, &v.AuthorName, &v.CreatedAt); err != nil {
			return nil, err
		}
		out = append(out, v)
	}
	return out, rows.Err()
}

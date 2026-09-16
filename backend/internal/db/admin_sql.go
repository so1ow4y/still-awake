package db

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"unicode"
)

type SQLResult struct {
	Kind         string           `json:"kind"`
	Columns      []string         `json:"columns,omitempty"`
	Rows         []map[string]any `json:"rows,omitempty"`
	RowsAffected int64            `json:"rows_affected,omitempty"`
	LastInsertID int64            `json:"last_insert_id,omitempty"`
}

type SchemaObject struct {
	Type  string `json:"type"`
	Name  string `json:"name"`
	Table string `json:"table"`
	SQL   string `json:"sql"`
}

func (s *Store) Schema(ctx context.Context) ([]SchemaObject, error) {
	rows, err := s.DB.QueryContext(ctx, `
SELECT type, name, tbl_name, COALESCE(sql, '')
FROM sqlite_master
WHERE type IN ('table','index','view','trigger')
  AND name NOT LIKE 'sqlite_%'
ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'view' THEN 1 WHEN 'index' THEN 2 ELSE 3 END, name`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []SchemaObject
	for rows.Next() {
		var v SchemaObject
		if err := rows.Scan(&v.Type, &v.Name, &v.Table, &v.SQL); err != nil {
			return nil, err
		}
		out = append(out, v)
	}
	return out, rows.Err()
}

func (s *Store) AdminSQL(ctx context.Context, query string, allowWrite bool) (SQLResult, error) {
	query = strings.TrimSpace(query)
	if query == "" {
		return SQLResult{}, errors.New("SQL query is empty")
	}
	if hasMultipleStatements(query) {
		return SQLResult{}, errors.New("only one SQL statement per request is allowed")
	}

	keyword := firstSQLKeyword(query)
	readOnly := keyword == "SELECT" || keyword == "PRAGMA" || keyword == "EXPLAIN"
	if readOnly {
		rows, err := s.DB.QueryContext(ctx, query)
		if err != nil {
			return SQLResult{}, err
		}
		defer rows.Close()
		cols, err := rows.Columns()
		if err != nil {
			return SQLResult{}, err
		}
		result := SQLResult{Kind: "query", Columns: cols, Rows: []map[string]any{}}
		for rows.Next() {
			vals := make([]any, len(cols))
			ptrs := make([]any, len(cols))
			for i := range vals {
				ptrs[i] = &vals[i]
			}
			if err := rows.Scan(ptrs...); err != nil {
				return SQLResult{}, err
			}
			item := make(map[string]any, len(cols))
			for i, c := range cols {
				item[c] = normalizeSQLValue(vals[i])
			}
			result.Rows = append(result.Rows, item)
			if len(result.Rows) >= 1000 {
				break
			}
		}
		return result, rows.Err()
	}

	if !allowWrite {
		return SQLResult{}, fmt.Errorf("%s is a write/admin statement; enable write SQL explicitly", keyword)
	}

	res, err := s.DB.ExecContext(ctx, query)
	if err != nil {
		return SQLResult{}, err
	}
	affected, _ := res.RowsAffected()
	lastID, _ := res.LastInsertId()
	return SQLResult{Kind: "exec", RowsAffected: affected, LastInsertID: lastID}, nil
}

func normalizeSQLValue(v any) any {
	switch x := v.(type) {
	case []byte:
		if json.Valid(x) {
			var decoded any
			if json.Unmarshal(x, &decoded) == nil {
				return decoded
			}
		}
		return string(x)
	default:
		return x
	}
}

func firstSQLKeyword(q string) string {
	q = strings.TrimSpace(stripLeadingSQLComments(q))
	var b strings.Builder
	for _, r := range q {
		if unicode.IsLetter(r) {
			b.WriteRune(unicode.ToUpper(r))
			continue
		}
		break
	}
	if b.Len() == 0 {
		return "UNKNOWN"
	}
	return b.String()
}

func stripLeadingSQLComments(q string) string {
	for {
		q = strings.TrimSpace(q)
		if strings.HasPrefix(q, "--") {
			if i := strings.IndexByte(q, '\n'); i >= 0 {
				q = q[i+1:]
				continue
			}
			return ""
		}
		if strings.HasPrefix(q, "/*") {
			if i := strings.Index(q[2:], "*/"); i >= 0 {
				q = q[i+4:]
				continue
			}
			return q
		}
		return q
	}
}

func hasMultipleStatements(q string) bool {
	q = strings.TrimSpace(q)
	q = strings.TrimSuffix(q, ";")
	// This intentionally errs on the safe side for the dev SQL console. A
	// semicolon inside a string literal will be rejected instead of trying to
	// implement a full SQL parser here.
	return strings.Contains(q, ";")
}

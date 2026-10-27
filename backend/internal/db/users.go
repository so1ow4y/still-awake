package db

import (
	"context"
	"database/sql"
	"errors"
	"sort"
	"strings"

	"still-awake-backend/internal/auth"
)

var ErrConflict = errors.New("уже занято")

const (
	KindStaff  = "staff"
	KindPlayer = "player"

	StatusActive  = "active"
	StatusBlocked = "blocked"
)

type RoleRef struct {
	ID       string `json:"id"`
	Slug     string `json:"slug"`
	Name     string `json:"name"`
	Priority int    `json:"priority"`
}

type User struct {
	ID                 string    `json:"id"`
	Username           string    `json:"username"`
	DisplayName        string    `json:"display_name"`
	Phone              string    `json:"phone,omitempty"`
	Kind               string    `json:"kind"`
	Status             string    `json:"status"`
	BlockedReason      string    `json:"blocked_reason,omitempty"`
	MustChangePassword bool      `json:"must_change_password"`
	HasPassword        bool      `json:"has_password"`
	InstallationID     string    `json:"installation_id,omitempty"`
	CreatedAt          string    `json:"created_at"`
	UpdatedAt          string    `json:"updated_at"`
	LastLoginAt        string    `json:"last_login_at,omitempty"`
	LastLoginIP        string    `json:"last_login_ip,omitempty"`
	Roles              []RoleRef `json:"roles"`
	PasswordHash       string    `json:"-"`
}

// TopPriority — уровень старшей роли пользователя (0 — ролей нет).
func (u User) TopPriority() int {
	top := 0
	for _, r := range u.Roles {
		if r.Priority > top {
			top = r.Priority
		}
	}
	return top
}

type Role struct {
	ID          string   `json:"id"`
	Slug        string   `json:"slug"`
	Name        string   `json:"name"`
	Description string   `json:"description"`
	Priority    int      `json:"priority"`
	IsSystem    bool     `json:"is_system"`
	Permissions []string `json:"permissions"`
	Users       int      `json:"users"`
	CreatedAt   string   `json:"created_at"`
	UpdatedAt   string   `json:"updated_at"`
}

type Permission struct {
	Slug        string `json:"slug"`
	Group       string `json:"group"`
	Description string `json:"description"`
}

type AuthSession struct {
	ID           string `json:"id"`
	UserID       string `json:"user_id"`
	Kind         string `json:"kind"`
	IP           string `json:"ip"`
	UserAgent    string `json:"user_agent"`
	CreatedAt    string `json:"created_at"`
	LastSeenAt   string `json:"last_seen_at"`
	ExpiresAt    string `json:"expires_at"`
	RevokedAt    string `json:"revoked_at,omitempty"`
	RevokeReason string `json:"revoke_reason,omitempty"`
	Current      bool   `json:"current"`
}

type LoginEvent struct {
	ID         int64  `json:"id"`
	UserID     string `json:"user_id"`
	Username   string `json:"username,omitempty"`
	Identifier string `json:"identifier"`
	Success    bool   `json:"success"`
	Reason     string `json:"reason,omitempty"`
	Method     string `json:"method"`
	IP         string `json:"ip"`
	UserAgent  string `json:"user_agent"`
	CreatedAt  string `json:"created_at"`
}

type UsernameChange struct {
	ID            int64  `json:"id"`
	OldUsername   string `json:"old_username"`
	NewUsername   string `json:"new_username"`
	ChangedBy     string `json:"changed_by"`
	ChangedByName string `json:"changed_by_name,omitempty"`
	CreatedAt     string `json:"created_at"`
}

// ---------------------------------------------------------------------- права и роли

// SeedAccessControl записывает каталог прав и пересобирает системные роли (как сид в license-store).
// Свои роли не трогаются; права, которых больше нет в каталоге, удаляются вместе с привязками.
func (s *Store) SeedAccessControl(ctx context.Context, perms []auth.PermissionDef, roles []auth.RoleDef) error {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()

	known := make([]any, 0, len(perms))
	for _, p := range perms {
		known = append(known, p.Slug)
		if _, err := tx.ExecContext(ctx, `INSERT INTO permissions(slug, grp, description) VALUES(?,?,?)
			ON CONFLICT(slug) DO UPDATE SET grp=excluded.grp, description=excluded.description`, p.Slug, p.Group, p.Description); err != nil {
			return err
		}
	}
	if _, err := tx.ExecContext(ctx, `DELETE FROM permissions WHERE slug NOT IN (`+placeholders(len(known))+`)`, known...); err != nil {
		return err
	}

	ts := now()
	for _, r := range roles {
		var id string
		err := tx.QueryRowContext(ctx, `SELECT id FROM roles WHERE slug=?`, r.Slug).Scan(&id)
		switch {
		case errors.Is(err, sql.ErrNoRows):
			id = auth.NewID("role")
			_, err = tx.ExecContext(ctx, `INSERT INTO roles(id, slug, name, description, priority, is_system, created_at, updated_at) VALUES(?,?,?,?,?,1,?,?)`,
				id, r.Slug, r.Name, r.Description, r.Priority, ts, ts)
		case err == nil:
			_, err = tx.ExecContext(ctx, `UPDATE roles SET name=?, description=?, priority=?, is_system=1, updated_at=? WHERE id=?`,
				r.Name, r.Description, r.Priority, ts, id)
		}
		if err != nil {
			return err
		}
		if _, err := tx.ExecContext(ctx, `DELETE FROM role_permissions WHERE role_id=?`, id); err != nil {
			return err
		}
		list := r.Permissions
		if list == nil {
			for _, p := range perms {
				list = append(list, p.Slug)
			}
		}
		for _, slug := range list {
			if _, err := tx.ExecContext(ctx, `INSERT INTO role_permissions(role_id, permission_slug) VALUES(?,?)`, id, slug); err != nil {
				return err
			}
		}
	}
	return tx.Commit()
}

func (s *Store) ListPermissions(ctx context.Context) ([]Permission, error) {
	rows, err := s.DB.QueryContext(ctx, `SELECT slug, grp, description FROM permissions ORDER BY grp, slug`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []Permission{}
	for rows.Next() {
		var p Permission
		if err := rows.Scan(&p.Slug, &p.Group, &p.Description); err != nil {
			return nil, err
		}
		out = append(out, p)
	}
	return out, rows.Err()
}

func (s *Store) ListRoles(ctx context.Context) ([]Role, error) {
	rows, err := s.DB.QueryContext(ctx, `SELECT r.id, r.slug, r.name, r.description, r.priority, r.is_system, r.created_at, r.updated_at,
		(SELECT COUNT(*) FROM user_roles ur WHERE ur.role_id = r.id),
		COALESCE((SELECT group_concat(permission_slug, ',') FROM role_permissions rp WHERE rp.role_id = r.id), '')
		FROM roles r ORDER BY r.priority DESC, r.name`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []Role{}
	for rows.Next() {
		r, err := scanRole(rows)
		if err != nil {
			return nil, err
		}
		out = append(out, r)
	}
	return out, rows.Err()
}

func (s *Store) GetRole(ctx context.Context, idOrSlug string) (Role, error) {
	row := s.DB.QueryRowContext(ctx, `SELECT r.id, r.slug, r.name, r.description, r.priority, r.is_system, r.created_at, r.updated_at,
		(SELECT COUNT(*) FROM user_roles ur WHERE ur.role_id = r.id),
		COALESCE((SELECT group_concat(permission_slug, ',') FROM role_permissions rp WHERE rp.role_id = r.id), '')
		FROM roles r WHERE r.id=? OR r.slug=?`, idOrSlug, idOrSlug)
	return scanRole(row)
}

type rowScanner interface{ Scan(dest ...any) error }

func scanRole(row rowScanner) (Role, error) {
	var r Role
	var system int
	var perms string
	if err := row.Scan(&r.ID, &r.Slug, &r.Name, &r.Description, &r.Priority, &system, &r.CreatedAt, &r.UpdatedAt, &r.Users, &perms); err != nil {
		return r, err
	}
	r.IsSystem = system == 1
	r.Permissions = splitList(perms)
	return r, nil
}

type RoleInput struct {
	Slug        string
	Name        string
	Description string
	Priority    int
	Permissions []string
}

func (s *Store) CreateRole(ctx context.Context, in RoleInput) (Role, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Role{}, err
	}
	defer tx.Rollback()
	id := auth.NewID("role")
	ts := now()
	if _, err := tx.ExecContext(ctx, `INSERT INTO roles(id, slug, name, description, priority, is_system, created_at, updated_at) VALUES(?,?,?,?,?,0,?,?)`,
		id, in.Slug, in.Name, in.Description, in.Priority, ts, ts); err != nil {
		if isUnique(err) {
			return Role{}, ErrConflict
		}
		return Role{}, err
	}
	if err := setRolePermissions(ctx, tx, id, in.Permissions); err != nil {
		return Role{}, err
	}
	if err := tx.Commit(); err != nil {
		return Role{}, err
	}
	return s.GetRole(ctx, id)
}

func (s *Store) UpdateRole(ctx context.Context, id string, in RoleInput, replacePermissions bool) (Role, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return Role{}, err
	}
	defer tx.Rollback()
	if _, err := tx.ExecContext(ctx, `UPDATE roles SET name=?, description=?, priority=?, updated_at=? WHERE id=? AND is_system=0`,
		in.Name, in.Description, in.Priority, now(), id); err != nil {
		return Role{}, err
	}
	if replacePermissions {
		if _, err := tx.ExecContext(ctx, `DELETE FROM role_permissions WHERE role_id=?`, id); err != nil {
			return Role{}, err
		}
		if err := setRolePermissions(ctx, tx, id, in.Permissions); err != nil {
			return Role{}, err
		}
	}
	if err := tx.Commit(); err != nil {
		return Role{}, err
	}
	return s.GetRole(ctx, id)
}

func setRolePermissions(ctx context.Context, tx *sql.Tx, roleID string, perms []string) error {
	for _, slug := range perms {
		if _, err := tx.ExecContext(ctx, `INSERT INTO role_permissions(role_id, permission_slug) VALUES(?,?)`, roleID, slug); err != nil {
			return err
		}
	}
	return nil
}

// DeleteRole удаляет свою роль. Игроки, у которых не остаётся ни одной роли, получают роль игрока.
// Возвращает, у скольких пользователей роль была.
func (s *Store) DeleteRole(ctx context.Context, id, actorID string) (int, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return 0, err
	}
	defer tx.Rollback()

	rows, err := tx.QueryContext(ctx, `SELECT ur.user_id, u.kind, (SELECT COUNT(*) FROM user_roles x WHERE x.user_id = ur.user_id)
		FROM user_roles ur JOIN users u ON u.id = ur.user_id WHERE ur.role_id=?`, id)
	if err != nil {
		return 0, err
	}
	type holder struct {
		id, kind string
		roles    int
	}
	var holders []holder
	for rows.Next() {
		var h holder
		if err := rows.Scan(&h.id, &h.kind, &h.roles); err != nil {
			rows.Close()
			return 0, err
		}
		holders = append(holders, h)
	}
	rows.Close()

	var playerRole string
	_ = tx.QueryRowContext(ctx, `SELECT id FROM roles WHERE slug=?`, auth.RolePlayer).Scan(&playerRole)
	for _, h := range holders {
		if h.roles <= 1 && h.kind == KindPlayer && playerRole != "" && playerRole != id {
			if _, err := tx.ExecContext(ctx, `INSERT OR IGNORE INTO user_roles(user_id, role_id, granted_by, granted_at) VALUES(?,?,?,?)`,
				h.id, playerRole, actorID, now()); err != nil {
				return 0, err
			}
		}
	}
	res, err := tx.ExecContext(ctx, `DELETE FROM roles WHERE id=? AND is_system=0`, id)
	if err != nil {
		return 0, err
	}
	if n, _ := res.RowsAffected(); n == 0 {
		return 0, sql.ErrNoRows
	}
	return len(holders), tx.Commit()
}

// UserAccess — права пользователя и уровень старшей роли.
func (s *Store) UserAccess(ctx context.Context, userID string) (map[string]bool, int, error) {
	rows, err := s.DB.QueryContext(ctx, `SELECT r.priority, COALESCE(rp.permission_slug, '')
		FROM user_roles ur JOIN roles r ON r.id = ur.role_id
		LEFT JOIN role_permissions rp ON rp.role_id = r.id
		WHERE ur.user_id=?`, userID)
	if err != nil {
		return nil, 0, err
	}
	defer rows.Close()
	perms := map[string]bool{}
	top := 0
	for rows.Next() {
		var priority int
		var slug string
		if err := rows.Scan(&priority, &slug); err != nil {
			return nil, 0, err
		}
		if priority > top {
			top = priority
		}
		if slug != "" {
			perms[slug] = true
		}
	}
	return perms, top, rows.Err()
}

// --------------------------------------------------------------------- пользователи

type NewUser struct {
	Username           string
	DisplayName        string
	Phone              string
	Kind               string
	PasswordHash       string
	MustChangePassword bool
	InstallationID     string
	RoleSlugs          []string
	GrantedBy          string
}

func (s *Store) CreateUser(ctx context.Context, in NewUser) (User, error) {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return User{}, err
	}
	defer tx.Rollback()
	id := auth.NewID("usr")
	ts := now()
	var phone any
	if in.Phone != "" {
		phone = in.Phone
	}
	if in.Kind == "" {
		in.Kind = KindStaff
	}
	if _, err := tx.ExecContext(ctx, `INSERT INTO users(id, username, display_name, phone, kind, password_hash, must_change_password, status, installation_id, created_at, updated_at)
		VALUES(?,?,?,?,?,?,?,?,?,?,?)`,
		id, in.Username, in.DisplayName, phone, in.Kind, in.PasswordHash, boolInt(in.MustChangePassword), StatusActive, in.InstallationID, ts, ts); err != nil {
		if isUnique(err) {
			return User{}, ErrConflict
		}
		return User{}, err
	}
	for _, slug := range in.RoleSlugs {
		if _, err := tx.ExecContext(ctx, `INSERT INTO user_roles(user_id, role_id, granted_by, granted_at) SELECT ?, id, ?, ? FROM roles WHERE slug=?`,
			id, in.GrantedBy, ts, slug); err != nil {
			return User{}, err
		}
	}
	if err := tx.Commit(); err != nil {
		return User{}, err
	}
	return s.GetUser(ctx, id)
}

const userColumns = `u.id, u.username, u.display_name, COALESCE(u.phone, ''), u.kind, u.status, u.blocked_reason, u.must_change_password,
	u.password_hash, u.installation_id, u.created_at, u.updated_at, u.last_login_at, u.last_login_ip`

func scanUser(row rowScanner) (User, error) {
	var u User
	var must int
	err := row.Scan(&u.ID, &u.Username, &u.DisplayName, &u.Phone, &u.Kind, &u.Status, &u.BlockedReason, &must,
		&u.PasswordHash, &u.InstallationID, &u.CreatedAt, &u.UpdatedAt, &u.LastLoginAt, &u.LastLoginIP)
	u.MustChangePassword = must == 1
	u.HasPassword = u.PasswordHash != ""
	u.Roles = []RoleRef{}
	return u, err
}

func (s *Store) GetUser(ctx context.Context, id string) (User, error) {
	u, err := scanUser(s.DB.QueryRowContext(ctx, `SELECT `+userColumns+` FROM users u WHERE u.id=?`, id))
	if err != nil {
		return u, err
	}
	return u, s.attachRoles(ctx, []*User{&u})
}

func (s *Store) GetUserByUsername(ctx context.Context, username string) (User, error) {
	u, err := scanUser(s.DB.QueryRowContext(ctx, `SELECT `+userColumns+` FROM users u WHERE u.username=?`, strings.TrimSpace(username)))
	if err != nil {
		return u, err
	}
	return u, s.attachRoles(ctx, []*User{&u})
}

func (s *Store) GetUserByPhone(ctx context.Context, phone string) (User, error) {
	u, err := scanUser(s.DB.QueryRowContext(ctx, `SELECT `+userColumns+` FROM users u WHERE u.phone=?`, phone))
	if err != nil {
		return u, err
	}
	return u, s.attachRoles(ctx, []*User{&u})
}

// GetUserByInstallation — игрок без телефона, которого узнают по ключу установки игры.
func (s *Store) GetUserByInstallation(ctx context.Context, installationID string) (User, error) {
	u, err := scanUser(s.DB.QueryRowContext(ctx, `SELECT `+userColumns+` FROM users u
		WHERE u.installation_id=? AND u.kind=? ORDER BY u.created_at LIMIT 1`, installationID, KindPlayer))
	if err != nil {
		return u, err
	}
	return u, s.attachRoles(ctx, []*User{&u})
}

func (s *Store) attachRoles(ctx context.Context, users []*User) error {
	if len(users) == 0 {
		return nil
	}
	ids := make([]any, len(users))
	byID := map[string]*User{}
	for i, u := range users {
		ids[i] = u.ID
		byID[u.ID] = u
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT ur.user_id, r.id, r.slug, r.name, r.priority FROM user_roles ur JOIN roles r ON r.id = ur.role_id
		WHERE ur.user_id IN (`+placeholders(len(ids))+`) ORDER BY r.priority DESC`, ids...)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var userID string
		var r RoleRef
		if err := rows.Scan(&userID, &r.ID, &r.Slug, &r.Name, &r.Priority); err != nil {
			return err
		}
		if u := byID[userID]; u != nil {
			u.Roles = append(u.Roles, r)
		}
	}
	return rows.Err()
}

type UserFilter struct {
	Query  string
	Kind   string
	Role   string
	Status string
	Limit  int
	Offset int
}

// ListUsers — поиск строкой: часть логина, имени или телефона; поле:значение для username:, phone:, role:, status:, kind:, id:.
func (s *Store) ListUsers(ctx context.Context, f UserFilter) ([]User, int, error) {
	if f.Limit <= 0 || f.Limit > 200 {
		f.Limit = 50
	}
	var where []string
	var args []any
	addTerm := func(field, value string) {
		like := "%" + value + "%"
		switch field {
		case "username":
			where = append(where, `u.username LIKE ?`)
			args = append(args, like)
		case "phone":
			where = append(where, `COALESCE(u.phone,'') LIKE ?`)
			args = append(args, "%"+strings.TrimPrefix(value, "+")+"%")
		case "role":
			where = append(where, `EXISTS (SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = u.id AND (r.slug = ? OR r.name LIKE ?))`)
			args = append(args, value, like)
		case "status":
			where = append(where, `u.status = ?`)
			args = append(args, value)
		case "kind":
			where = append(where, `u.kind = ?`)
			args = append(args, value)
		case "id":
			where = append(where, `u.id = ?`)
			args = append(args, value)
		default:
			where = append(where, `(u.username LIKE ? OR u.display_name LIKE ? OR COALESCE(u.phone,'') LIKE ? OR u.id = ?
				OR EXISTS (SELECT 1 FROM username_history h WHERE h.user_id = u.id AND h.old_username LIKE ?))`)
			args = append(args, like, like, like, value, like)
		}
	}
	for _, token := range strings.Fields(f.Query) {
		if field, value, ok := strings.Cut(token, ":"); ok && value != "" {
			addTerm(strings.ToLower(field), value)
		} else {
			addTerm("", token)
		}
	}
	if f.Kind != "" {
		addTerm("kind", f.Kind)
	}
	if f.Role != "" {
		addTerm("role", f.Role)
	}
	if f.Status != "" {
		addTerm("status", f.Status)
	}
	cond := ""
	if len(where) > 0 {
		cond = " WHERE " + strings.Join(where, " AND ")
	}
	var total int
	if err := s.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM users u`+cond, args...).Scan(&total); err != nil {
		return nil, 0, err
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT `+userColumns+` FROM users u`+cond+` ORDER BY u.created_at DESC LIMIT ? OFFSET ?`,
		append(args, f.Limit, f.Offset)...)
	if err != nil {
		return nil, 0, err
	}
	defer rows.Close()
	out := []User{}
	for rows.Next() {
		u, err := scanUser(rows)
		if err != nil {
			return nil, 0, err
		}
		out = append(out, u)
	}
	if err := rows.Err(); err != nil {
		return nil, 0, err
	}
	ptrs := make([]*User, len(out))
	for i := range out {
		ptrs[i] = &out[i]
	}
	return out, total, s.attachRoles(ctx, ptrs)
}

// ChangeUsername меняет логин и записывает старый в историю — в одной транзакции.
func (s *Store) ChangeUsername(ctx context.Context, userID, username, changedBy string) error {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	var old string
	if err := tx.QueryRowContext(ctx, `SELECT username FROM users WHERE id=?`, userID).Scan(&old); err != nil {
		return err
	}
	if old == username {
		return nil
	}
	if _, err := tx.ExecContext(ctx, `UPDATE users SET username=?, updated_at=? WHERE id=?`, username, now(), userID); err != nil {
		if isUnique(err) {
			return ErrConflict
		}
		return err
	}
	if _, err := tx.ExecContext(ctx, `INSERT INTO username_history(user_id, old_username, new_username, changed_by, created_at) VALUES(?,?,?,?,?)`,
		userID, old, username, changedBy, now()); err != nil {
		return err
	}
	return tx.Commit()
}

func (s *Store) ListUsernameHistory(ctx context.Context, userID string) ([]UsernameChange, error) {
	rows, err := s.DB.QueryContext(ctx, `SELECT h.id, h.old_username, h.new_username, h.changed_by, COALESCE(u.username, ''), h.created_at
		FROM username_history h LEFT JOIN users u ON u.id = h.changed_by WHERE h.user_id=? ORDER BY h.id DESC`, userID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []UsernameChange{}
	for rows.Next() {
		var c UsernameChange
		if err := rows.Scan(&c.ID, &c.OldUsername, &c.NewUsername, &c.ChangedBy, &c.ChangedByName, &c.CreatedAt); err != nil {
			return nil, err
		}
		out = append(out, c)
	}
	return out, rows.Err()
}

func (s *Store) UpdateUserProfile(ctx context.Context, userID, displayName string, phone *string) error {
	q := `UPDATE users SET display_name=?, updated_at=?`
	args := []any{displayName, now()}
	if phone != nil {
		var v any
		if *phone != "" {
			v = *phone
		}
		q += `, phone=?`
		args = append(args, v)
	}
	res, err := s.DB.ExecContext(ctx, q+` WHERE id=?`, append(args, userID)...)
	if err != nil {
		if isUnique(err) {
			return ErrConflict
		}
		return err
	}
	return affected(res)
}

func (s *Store) SetPassword(ctx context.Context, userID, hash string, mustChange bool) error {
	res, err := s.DB.ExecContext(ctx, `UPDATE users SET password_hash=?, must_change_password=?, updated_at=? WHERE id=?`,
		hash, boolInt(mustChange), now(), userID)
	if err != nil {
		return err
	}
	return affected(res)
}

func (s *Store) SetUserStatus(ctx context.Context, userID, status, reason string) error {
	res, err := s.DB.ExecContext(ctx, `UPDATE users SET status=?, blocked_reason=?, updated_at=? WHERE id=?`, status, reason, now(), userID)
	if err != nil {
		return err
	}
	return affected(res)
}

func (s *Store) SetUserInstallation(ctx context.Context, userID, installationID string) error {
	_, err := s.DB.ExecContext(ctx, `UPDATE users SET installation_id=?, updated_at=? WHERE id=?`, installationID, now(), userID)
	return err
}

func (s *Store) MarkLogin(ctx context.Context, userID, ip string) error {
	_, err := s.DB.ExecContext(ctx, `UPDATE users SET last_login_at=?, last_login_ip=? WHERE id=?`, now(), ip, userID)
	return err
}

func (s *Store) SetUserRoles(ctx context.Context, userID string, roleIDs []string, grantedBy string) error {
	tx, err := s.DB.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if _, err := tx.ExecContext(ctx, `DELETE FROM user_roles WHERE user_id=?`, userID); err != nil {
		return err
	}
	ts := now()
	for _, id := range roleIDs {
		if _, err := tx.ExecContext(ctx, `INSERT INTO user_roles(user_id, role_id, granted_by, granted_at) VALUES(?,?,?,?)`, userID, id, grantedBy, ts); err != nil {
			return err
		}
	}
	return tx.Commit()
}

func (s *Store) CountUsersWithRole(ctx context.Context, slug string) (int, error) {
	var n int
	err := s.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE r.slug=?`, slug).Scan(&n)
	return n, err
}

func (s *Store) FirstUserWithRole(ctx context.Context, slug string) (User, error) {
	var id string
	err := s.DB.QueryRowContext(ctx, `SELECT ur.user_id FROM user_roles ur JOIN roles r ON r.id = ur.role_id JOIN users u ON u.id = ur.user_id
		WHERE r.slug=? ORDER BY u.created_at LIMIT 1`, slug).Scan(&id)
	if err != nil {
		return User{}, err
	}
	return s.GetUser(ctx, id)
}

// ------------------------------------------------------------------------ сессии входа

func (s *Store) CreateAuthSession(ctx context.Context, v AuthSession, tokenHash string) error {
	_, err := s.DB.ExecContext(ctx, `INSERT INTO auth_sessions(id, user_id, token_hash, kind, ip, user_agent, created_at, last_seen_at, expires_at)
		VALUES(?,?,?,?,?,?,?,?,?)`, v.ID, v.UserID, tokenHash, v.Kind, v.IP, v.UserAgent, v.CreatedAt, v.CreatedAt, v.ExpiresAt)
	return err
}

// ActiveSessionByToken — живая (не отозванная и не истёкшая) сессия по хешу токена.
func (s *Store) ActiveSessionByToken(ctx context.Context, tokenHash, nowTS string) (AuthSession, error) {
	var v AuthSession
	err := s.DB.QueryRowContext(ctx, `SELECT id, user_id, kind, ip, user_agent, created_at, last_seen_at, expires_at FROM auth_sessions
		WHERE token_hash=? AND revoked_at='' AND expires_at > ?`, tokenHash, nowTS).
		Scan(&v.ID, &v.UserID, &v.Kind, &v.IP, &v.UserAgent, &v.CreatedAt, &v.LastSeenAt, &v.ExpiresAt)
	return v, err
}

func (s *Store) TouchAuthSession(ctx context.Context, id, lastSeen, expiresAt string) error {
	_, err := s.DB.ExecContext(ctx, `UPDATE auth_sessions SET last_seen_at=?, expires_at=? WHERE id=?`, lastSeen, expiresAt, id)
	return err
}

func (s *Store) RevokeAuthSession(ctx context.Context, userID, id, reason string) error {
	res, err := s.DB.ExecContext(ctx, `UPDATE auth_sessions SET revoked_at=?, revoke_reason=? WHERE id=? AND user_id=? AND revoked_at=''`, now(), reason, id, userID)
	if err != nil {
		return err
	}
	return affected(res)
}

// RevokeUserSessions завершает все сессии пользователя, кроме exceptID. Возвращает, сколько завершено.
func (s *Store) RevokeUserSessions(ctx context.Context, userID, reason, exceptID string) (int64, error) {
	res, err := s.DB.ExecContext(ctx, `UPDATE auth_sessions SET revoked_at=?, revoke_reason=? WHERE user_id=? AND revoked_at='' AND id<>?`,
		now(), reason, userID, exceptID)
	if err != nil {
		return 0, err
	}
	return res.RowsAffected()
}

func (s *Store) ListUserSessions(ctx context.Context, userID string, limit int) ([]AuthSession, error) {
	if limit <= 0 || limit > 200 {
		limit = 50
	}
	rows, err := s.DB.QueryContext(ctx, `SELECT id, user_id, kind, ip, user_agent, created_at, last_seen_at, expires_at, revoked_at, revoke_reason
		FROM auth_sessions WHERE user_id=? ORDER BY last_seen_at DESC LIMIT ?`, userID, limit)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []AuthSession{}
	for rows.Next() {
		var v AuthSession
		if err := rows.Scan(&v.ID, &v.UserID, &v.Kind, &v.IP, &v.UserAgent, &v.CreatedAt, &v.LastSeenAt, &v.ExpiresAt, &v.RevokedAt, &v.RevokeReason); err != nil {
			return nil, err
		}
		out = append(out, v)
	}
	return out, rows.Err()
}

// ------------------------------------------------------------------------ журнал входов

func (s *Store) AddLoginEvent(ctx context.Context, e LoginEvent) error {
	_, err := s.DB.ExecContext(ctx, `INSERT INTO login_history(user_id, identifier, success, reason, method, ip, user_agent, created_at) VALUES(?,?,?,?,?,?,?,?)`,
		e.UserID, e.Identifier, boolInt(e.Success), e.Reason, e.Method, e.IP, e.UserAgent, now())
	return err
}

// RecentLoginFailures — сколько неудачных попыток с этого адреса или по этому логину с момента since.
func (s *Store) RecentLoginFailures(ctx context.Context, identifier, ip, since string) (int, error) {
	var n int
	err := s.DB.QueryRowContext(ctx, `SELECT COUNT(*) FROM login_history WHERE success=0 AND created_at > ? AND (identifier=? COLLATE NOCASE OR ip=?)`,
		since, identifier, ip).Scan(&n)
	return n, err
}

type LoginFilter struct {
	UserID  string
	Query   string
	Success *bool
	Limit   int
}

func (s *Store) ListLoginEvents(ctx context.Context, f LoginFilter) ([]LoginEvent, error) {
	if f.Limit <= 0 || f.Limit > 500 {
		f.Limit = 100
	}
	q := `SELECT l.id, l.user_id, COALESCE(u.username, ''), l.identifier, l.success, l.reason, l.method, l.ip, l.user_agent, l.created_at
		FROM login_history l LEFT JOIN users u ON u.id = l.user_id`
	var where []string
	var args []any
	if f.UserID != "" {
		where = append(where, `l.user_id=?`)
		args = append(args, f.UserID)
	}
	if v := strings.TrimSpace(f.Query); v != "" {
		like := "%" + v + "%"
		where = append(where, `(l.identifier LIKE ? OR l.ip LIKE ? OR COALESCE(u.username,'') LIKE ? OR l.reason LIKE ?)`)
		args = append(args, like, like, like, like)
	}
	if f.Success != nil {
		where = append(where, `l.success=?`)
		args = append(args, boolInt(*f.Success))
	}
	if len(where) > 0 {
		q += ` WHERE ` + strings.Join(where, " AND ")
	}
	rows, err := s.DB.QueryContext(ctx, q+` ORDER BY l.id DESC LIMIT ?`, append(args, f.Limit)...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []LoginEvent{}
	for rows.Next() {
		var e LoginEvent
		var success int
		if err := rows.Scan(&e.ID, &e.UserID, &e.Username, &e.Identifier, &success, &e.Reason, &e.Method, &e.IP, &e.UserAgent, &e.CreatedAt); err != nil {
			return nil, err
		}
		e.Success = success == 1
		out = append(out, e)
	}
	return out, rows.Err()
}

// --------------------------------------------------------------------------- мелочи

func placeholders(n int) string {
	if n <= 0 {
		return "NULL"
	}
	return strings.TrimSuffix(strings.Repeat("?,", n), ",")
}

func splitList(v string) []string {
	if v == "" {
		return []string{}
	}
	out := strings.Split(v, ",")
	sort.Strings(out)
	return out
}

func boolInt(v bool) int {
	if v {
		return 1
	}
	return 0
}

func isUnique(err error) bool {
	return err != nil && strings.Contains(err.Error(), "UNIQUE constraint failed")
}

func affected(res sql.Result) error {
	if n, _ := res.RowsAffected(); n == 0 {
		return sql.ErrNoRows
	}
	return nil
}

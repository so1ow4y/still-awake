package httpapi

import (
	"database/sql"
	"errors"
	"net/http"
	"regexp"
	"strconv"
	"strings"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

// Управление пользователями и ролями — поведение license-store (admin-users/admin-roles.service):
//   - трогать можно только тех, чья старшая роль НИЖЕ твоей, и не себя (себя — в профиле);
//   - выдавать и снимать можно только роли ниже своей;
//   - в свою роль включаются только права, которые есть у тебя, уровень — ниже своего;
//   - системные роли только читаются, на их основе можно собрать свою.

var roleSlugRe = regexp.MustCompile(`^[a-z][a-z0-9_]{2,63}$`)

// manageable — цель существует, это не ты, и её старшая роль ниже твоей.
func (s *Server) manageable(r *http.Request, a *Actor, userID string) (db.User, error) {
	if userID == a.User.ID {
		return db.User{}, fail(http.StatusForbidden, "CANNOT_MANAGE_SELF", "Свой аккаунт меняйте в профиле")
	}
	target, err := s.store.GetUser(r.Context(), userID)
	if err != nil {
		return target, err
	}
	if target.TopPriority() >= a.Priority {
		return target, fail(http.StatusForbidden, "USER_TOO_PRIVILEGED", "Нельзя менять пользователя с ролью не ниже вашей")
	}
	return target, nil
}

// rolesBelow превращает коды ролей в роли и проверяет, что все они ниже уровня актора.
func (s *Server) rolesBelow(r *http.Request, a *Actor, slugs []string) ([]db.Role, error) {
	seen := map[string]bool{}
	var out []db.Role
	for _, slug := range slugs {
		slug = strings.TrimSpace(slug)
		if slug == "" || seen[slug] {
			continue
		}
		seen[slug] = true
		role, err := s.store.GetRole(r.Context(), slug)
		if errors.Is(err, sql.ErrNoRows) {
			return nil, fail(http.StatusBadRequest, "ROLE_NOT_FOUND", "Неизвестная роль: "+slug)
		}
		if err != nil {
			return nil, err
		}
		out = append(out, role)
	}
	return out, nil
}

func (s *Server) listUsers(w http.ResponseWriter, r *http.Request, a *Actor) {
	q := r.URL.Query()
	limit, _ := strconv.Atoi(q.Get("limit"))
	offset, _ := strconv.Atoi(q.Get("offset"))
	rows, total, err := s.store.ListUsers(r.Context(), db.UserFilter{
		Query: q.Get("q"), Kind: q.Get("kind"), Role: q.Get("role"), Status: q.Get("status"), Limit: limit, Offset: offset,
	})
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"items": rows, "total": total})
}

func (s *Server) getUserCard(w http.ResponseWriter, r *http.Request, a *Actor) {
	id := r.PathValue("id")
	user, err := s.store.GetUser(r.Context(), id)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	history, err := s.store.ListUsernameHistory(r.Context(), id)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	card := map[string]any{"user": user, "usernames": history, "manageable": id != a.User.ID && user.TopPriority() < a.Priority}
	if a.Can(auth.PermLoginsRead) {
		logins, err := s.store.ListLoginEvents(r.Context(), db.LoginFilter{UserID: id, Limit: 20})
		if err != nil {
			writeAPIError(w, err)
			return
		}
		card["logins"] = logins
	}
	sessions, err := s.store.ListUserSessions(r.Context(), id, 20)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	card["sessions"] = sessions
	writeJSON(w, http.StatusOK, card)
}

// createUser — сотрудник с логином и паролем, без почты. Пустой пароль — сгенерировать (вернётся один раз).
func (s *Server) createUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in struct {
		Username           string   `json:"username"`
		DisplayName        string   `json:"display_name"`
		Password           string   `json:"password"`
		Roles              []string `json:"roles"`
		MustChangePassword *bool    `json:"must_change_password"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	username, err := auth.NormalizeUsername(in.Username)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
		return
	}
	roles, err := s.rolesBelow(r, a, in.Roles)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if len(roles) == 0 {
		writeAPIError(w, fail(http.StatusBadRequest, "ROLE_REQUIRED", "Нужна хотя бы одна роль"))
		return
	}
	if !a.Can(auth.PermRolesAssign) {
		writeAPIError(w, fail(http.StatusForbidden, "FORBIDDEN", "Для выдачи ролей нужно право roles.assign"))
		return
	}
	slugs := make([]string, 0, len(roles))
	for _, role := range roles {
		if role.Priority >= a.Priority {
			writeAPIError(w, fail(http.StatusForbidden, "ROLE_TOO_HIGH", "Можно назначать только роли ниже своей"))
			return
		}
		slugs = append(slugs, role.Slug)
	}
	password, generated := in.Password, ""
	if password == "" {
		password = auth.RandomPassword(14)
		generated = password
	}
	hash, err := auth.HashPassword(password)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
		return
	}
	mustChange := true
	if in.MustChangePassword != nil {
		mustChange = *in.MustChangePassword
	}
	user, err := s.store.CreateUser(r.Context(), db.NewUser{
		Username: username, DisplayName: cleanName(in.DisplayName), Kind: db.KindStaff,
		PasswordHash: hash, MustChangePassword: mustChange, RoleSlugs: slugs, GrantedBy: a.User.ID,
	})
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusCreated, map[string]any{"user": user, "generated_password": generated})
}

func (s *Server) updateUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	var in struct {
		Username    *string `json:"username"`
		DisplayName *string `json:"display_name"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	if in.Username != nil {
		username, err := auth.NormalizeUsername(*in.Username)
		if err != nil && target.Kind == db.KindStaff {
			writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
			return
		}
		if err != nil {
			username = strings.TrimSpace(*in.Username)
		}
		if err := s.store.ChangeUsername(r.Context(), target.ID, username, a.User.ID); err != nil {
			writeAPIError(w, err)
			return
		}
	}
	if in.DisplayName != nil {
		if err := s.store.UpdateUserProfile(r.Context(), target.ID, cleanName(*in.DisplayName)); err != nil {
			writeAPIError(w, err)
			return
		}
	}
	s.respondUser(w, r, target.ID)
}

// deleteUser — удалить пользователя ниже себя по уровню (себя нельзя). ?with_data=1 — вместе с установкой игры
// и всеми ночами игрока; без него ночи остаются в «Игровых сессиях» без владельца.
func (s *Server) deleteUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	withData := r.URL.Query().Get("with_data") == "1"
	nights, err := s.store.DeleteUser(r.Context(), target.ID, withData)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "deleted_sessions": nights})
}

func (s *Server) setUserPassword(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	var in struct {
		Password           string `json:"password"`
		MustChangePassword *bool  `json:"must_change_password"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	password, generated := in.Password, ""
	if password == "" {
		password = auth.RandomPassword(14)
		generated = password
	}
	hash, err := auth.HashPassword(password)
	if err != nil {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", err.Error()))
		return
	}
	mustChange := true
	if in.MustChangePassword != nil {
		mustChange = *in.MustChangePassword
	}
	if err := s.store.SetPassword(r.Context(), target.ID, hash, mustChange); err != nil {
		writeAPIError(w, err)
		return
	}
	revoked, _ := s.store.RevokeUserSessions(r.Context(), target.ID, "password_reset", "")
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "generated_password": generated, "revoked_sessions": revoked})
}

func (s *Server) blockUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	var in struct {
		Reason string `json:"reason"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	reason := strings.TrimSpace(in.Reason)
	if reason == "" {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Укажите причину блокировки"))
		return
	}
	if err := s.store.SetUserStatus(r.Context(), target.ID, db.StatusBlocked, reason); err != nil {
		writeAPIError(w, err)
		return
	}
	// Без отзыва сессий заблокированный работал бы до их истечения.
	_, _ = s.store.RevokeUserSessions(r.Context(), target.ID, "blocked", "")
	s.respondUser(w, r, target.ID)
}

func (s *Server) unblockUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if target.Status != db.StatusBlocked {
		writeAPIError(w, fail(http.StatusConflict, "NOT_BLOCKED", "Пользователь не заблокирован"))
		return
	}
	if err := s.store.SetUserStatus(r.Context(), target.ID, db.StatusActive, ""); err != nil {
		writeAPIError(w, err)
		return
	}
	s.respondUser(w, r, target.ID)
}

func (s *Server) logoutUser(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	n, err := s.store.RevokeUserSessions(r.Context(), target.ID, "admin_logout", "")
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "revoked_sessions": n})
}

func (s *Server) setUserRoles(w http.ResponseWriter, r *http.Request, a *Actor) {
	target, err := s.manageable(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	var in struct {
		Roles []string `json:"roles"`
	}
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	roles, err := s.rolesBelow(r, a, in.Roles)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	if len(roles) == 0 {
		writeAPIError(w, fail(http.StatusBadRequest, "ROLE_REQUIRED", "Нужна хотя бы одна роль"))
		return
	}
	// Нельзя ни выдать, ни снять роль не ниже своей.
	next := map[string]db.Role{}
	ids := make([]string, 0, len(roles))
	for _, role := range roles {
		next[role.Slug] = role
		ids = append(ids, role.ID)
	}
	current := map[string]bool{}
	for _, role := range target.Roles {
		current[role.Slug] = true
		if _, kept := next[role.Slug]; !kept && role.Priority >= a.Priority {
			writeAPIError(w, fail(http.StatusForbidden, "ROLE_TOO_HIGH", "Можно снимать только роли ниже своей"))
			return
		}
	}
	for slug, role := range next {
		if !current[slug] && role.Priority >= a.Priority {
			writeAPIError(w, fail(http.StatusForbidden, "ROLE_TOO_HIGH", "Можно назначать только роли ниже своей"))
			return
		}
	}
	if err := s.store.SetUserRoles(r.Context(), target.ID, ids, a.User.ID); err != nil {
		writeAPIError(w, err)
		return
	}
	s.respondUser(w, r, target.ID)
}

func (s *Server) respondUser(w http.ResponseWriter, r *http.Request, id string) {
	user, err := s.store.GetUser(r.Context(), id)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, user)
}

// ----------------------------------------------------------------------------- роли

func (s *Server) listRoles(w http.ResponseWriter, r *http.Request, a *Actor) {
	rows, err := s.store.ListRoles(r.Context())
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

func (s *Server) listPermissions(w http.ResponseWriter, r *http.Request, a *Actor) {
	rows, err := s.store.ListPermissions(r.Context())
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

type roleBody struct {
	Slug        string   `json:"slug"`
	Name        *string  `json:"name"`
	Description *string  `json:"description"`
	Priority    *int     `json:"priority"`
	Permissions []string `json:"permissions"`
}

// checkRolePermissions: права существуют, их хотя бы одно и все они есть у создателя.
func (s *Server) checkRolePermissions(r *http.Request, a *Actor, slugs []string) ([]string, error) {
	all, err := s.store.ListPermissions(r.Context())
	if err != nil {
		return nil, err
	}
	known := map[string]bool{}
	for _, p := range all {
		known[p.Slug] = true
	}
	seen := map[string]bool{}
	var out, foreign []string
	for _, slug := range slugs {
		if seen[slug] {
			continue
		}
		seen[slug] = true
		if !known[slug] {
			return nil, fail(http.StatusBadRequest, "PERMISSION_NOT_FOUND", "Неизвестное право: "+slug)
		}
		if !a.Can(slug) {
			foreign = append(foreign, slug)
		}
		out = append(out, slug)
	}
	if len(out) == 0 {
		return nil, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Выберите хотя бы одно право")
	}
	if len(foreign) > 0 {
		return nil, fail(http.StatusForbidden, "PERMISSION_NOT_OWNED", "В роль можно включать только права, которые есть у вас: "+strings.Join(foreign, ", "))
	}
	return out, nil
}

func checkRolePriority(a *Actor, priority int) error {
	if priority < auth.CustomRoleMin || priority > auth.CustomRoleMax {
		return fail(http.StatusBadRequest, "ROLE_PRIORITY_INVALID", "Уровень роли — от "+strconv.Itoa(auth.CustomRoleMin)+" до "+strconv.Itoa(auth.CustomRoleMax))
	}
	if priority >= a.Priority {
		return fail(http.StatusForbidden, "ROLE_TOO_HIGH", "Уровень роли должен быть ниже вашего ("+strconv.Itoa(a.Priority)+")")
	}
	return nil
}

func (s *Server) createRole(w http.ResponseWriter, r *http.Request, a *Actor) {
	var in roleBody
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	slug := strings.ToLower(strings.TrimSpace(in.Slug))
	if !roleSlugRe.MatchString(slug) {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Код роли: 3–64 символа, латиница, цифры и _"))
		return
	}
	name := ""
	if in.Name != nil {
		name = strings.TrimSpace(*in.Name)
	}
	if name == "" {
		writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Укажите название роли"))
		return
	}
	perms, err := s.checkRolePermissions(r, a, in.Permissions)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	priority := auth.CustomRoleMin
	if in.Priority != nil {
		priority = *in.Priority
	}
	if err := checkRolePriority(a, priority); err != nil {
		writeAPIError(w, err)
		return
	}
	desc := ""
	if in.Description != nil {
		desc = strings.TrimSpace(*in.Description)
	}
	role, err := s.store.CreateRole(r.Context(), db.RoleInput{Slug: slug, Name: truncate(name, 128), Description: truncate(desc, 500), Priority: priority, Permissions: perms})
	if errors.Is(err, db.ErrConflict) {
		writeAPIError(w, fail(http.StatusConflict, "ALREADY_EXISTS", "Роль с таким кодом уже есть"))
		return
	}
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusCreated, role)
}

// editableRole — своя роль ниже своего уровня.
func (s *Server) editableRole(r *http.Request, a *Actor, id string) (db.Role, error) {
	role, err := s.store.GetRole(r.Context(), id)
	if err != nil {
		return role, err
	}
	if role.IsSystem {
		return role, fail(http.StatusForbidden, "ROLE_SYSTEM", "Системные роли меняются только в коде — создайте свою на их основе")
	}
	if role.Priority >= a.Priority {
		return role, fail(http.StatusForbidden, "ROLE_TOO_HIGH", "Можно менять только роли ниже своей")
	}
	return role, nil
}

func (s *Server) updateRole(w http.ResponseWriter, r *http.Request, a *Actor) {
	role, err := s.editableRole(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	var in roleBody
	if err := decodeJSON(r, &in); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}
	next := db.RoleInput{Name: role.Name, Description: role.Description, Priority: role.Priority, Permissions: role.Permissions}
	if in.Name != nil {
		if next.Name = strings.TrimSpace(*in.Name); next.Name == "" {
			writeAPIError(w, fail(http.StatusBadRequest, "VALIDATION_ERROR", "Укажите название роли"))
			return
		}
		next.Name = truncate(next.Name, 128)
	}
	if in.Description != nil {
		next.Description = truncate(strings.TrimSpace(*in.Description), 500)
	}
	if in.Priority != nil {
		next.Priority = *in.Priority
	}
	if err := checkRolePriority(a, next.Priority); err != nil {
		writeAPIError(w, err)
		return
	}
	replace := in.Permissions != nil
	if replace {
		if next.Permissions, err = s.checkRolePermissions(r, a, in.Permissions); err != nil {
			writeAPIError(w, err)
			return
		}
	}
	updated, err := s.store.UpdateRole(r.Context(), role.ID, next, replace)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, updated)
}

func (s *Server) deleteRole(w http.ResponseWriter, r *http.Request, a *Actor) {
	role, err := s.editableRole(r, a, r.PathValue("id"))
	if err != nil {
		writeAPIError(w, err)
		return
	}
	users, err := s.store.DeleteRole(r.Context(), role.ID, a.User.ID)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"ok": true, "users": users})
}

// ----------------------------------------------------------------------- журнал входов

func (s *Server) listLogins(w http.ResponseWriter, r *http.Request, a *Actor) {
	q := r.URL.Query()
	limit, _ := strconv.Atoi(q.Get("limit"))
	f := db.LoginFilter{UserID: q.Get("user_id"), Query: q.Get("q"), Limit: limit}
	switch q.Get("success") {
	case "true", "1":
		v := true
		f.Success = &v
	case "false", "0":
		v := false
		f.Success = &v
	}
	rows, err := s.store.ListLoginEvents(r.Context(), f)
	if err != nil {
		writeAPIError(w, err)
		return
	}
	writeJSON(w, http.StatusOK, rows)
}

func truncate(v string, n int) string {
	if r := []rune(v); len(r) > n {
		return string(r[:n])
	}
	return v
}

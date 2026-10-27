package main

import (
	"bufio"
	"context"
	"database/sql"
	"errors"
	"fmt"
	"io/fs"
	"log"
	"os"
	"path/filepath"
	"strings"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

// adminBootstrap — первый супер-админ.
//
//	ADMIN_USERNAME          логин (admin)
//	ADMIN_INITIAL_PASSWORD  первый пароль; без него — из ADMIN_PASSWORD_FILE, если файл уже есть, иначе случайный
//	ADMIN_PASSWORD_FILE     куда сохранить сгенерированный первый пароль, чтобы не терять его между запусками
//	DEV_MODE                печатать первый пароль при каждом запуске, а не только при создании админа
//	ADMIN_RESET_PASSWORD    вернуть супер-админу первый пароль (или выдать новый), если доступ потерян
type adminBootstrap struct {
	username string
	password string // первый пароль из окружения или файла; пусто — неизвестен
	source   string // откуда взят пароль — для лога
	file     string
	dev      bool
	reset    bool
}

func adminBootstrapFromEnv() (adminBootstrap, error) {
	b := adminBootstrap{
		username: env("ADMIN_USERNAME", "admin"),
		file:     strings.TrimSpace(os.Getenv("ADMIN_PASSWORD_FILE")),
		dev:      envBool("DEV_MODE", false),
		reset:    envBool("ADMIN_RESET_PASSWORD", false),
	}
	// ADMIN_PASSWORD — прежнее имя переменной, оставлено для старых .env.
	if v := env("ADMIN_INITIAL_PASSWORD", os.Getenv("ADMIN_PASSWORD")); v != "" {
		b.password, b.source = v, "ADMIN_INITIAL_PASSWORD"
		return b, nil
	}
	if b.file == "" {
		return b, nil
	}
	password, err := readPasswordFile(b.file)
	switch {
	case err == nil:
		b.password, b.source = password, "файл "+b.file
	case !errors.Is(err, fs.ErrNotExist):
		return b, fmt.Errorf("ADMIN_PASSWORD_FILE: %w", err)
	}
	return b, nil
}

func ensureSuperAdmin(ctx context.Context, store *db.Store, b adminBootstrap) error {
	if b.dev {
		log.Printf("DEV_MODE=true: первый пароль супер-админа печатается при каждом запуске. На сервере для игроков выключите.")
	}
	existing, err := store.FirstUserWithRole(ctx, auth.RoleSuperAdmin)
	switch {
	case errors.Is(err, sql.ErrNoRows):
		return b.create(ctx, store)
	case err != nil:
		return err
	case b.reset:
		return b.resetPassword(ctx, store, existing, "Пароль супер-админа сброшен (ADMIN_RESET_PASSWORD=true)")
	case !b.dev:
		return nil
	case b.password == "" && existing.MustChangePassword:
		// Первый пароль нигде не сохранён, но его ещё не меняли — выдаём новый, чтобы dev-режим не оставил без входа.
		return b.resetPassword(ctx, store, existing, "DEV_MODE: первый пароль не был сохранён — выдан новый")
	case b.password == "":
		log.Printf("DEV_MODE: первый пароль супер-админа %q неизвестен: нет ADMIN_INITIAL_PASSWORD и файла ADMIN_PASSWORD_FILE, "+
			"а пароль уже меняли. Вернуть доступ: ADMIN_RESET_PASSWORD=true.", existing.Username)
		return nil
	default:
		state := "действует"
		if !auth.VerifyPassword(existing.PasswordHash, b.password) {
			state = "уже изменён — входите с новым паролем"
		}
		announce("DEV_MODE: первый пароль супер-админа ("+state+")", existing.Username, b.password, b.source, true)
		return nil
	}
}

// initialPassword — пароль из окружения или файла, иначе новый случайный.
func (b adminBootstrap) initialPassword() (password, source string, generated bool) {
	if b.password != "" {
		return b.password, b.source, false
	}
	return auth.RandomPassword(16), "сгенерирован", true
}

func (b adminBootstrap) create(ctx context.Context, store *db.Store) error {
	if _, err := auth.NormalizeUsername(b.username); err != nil {
		return fmt.Errorf("ADMIN_USERNAME: %w", err)
	}
	password, source, generated := b.initialPassword()
	hash, err := auth.HashPassword(password)
	if err != nil {
		return fmt.Errorf("первый пароль (%s): %w", source, err)
	}
	user, err := store.CreateUser(ctx, db.NewUser{
		Username: b.username, DisplayName: "Администратор", Kind: db.KindStaff,
		PasswordHash: hash, MustChangePassword: generated, RoleSlugs: []string{auth.RoleSuperAdmin},
	})
	if errors.Is(err, db.ErrConflict) {
		return fmt.Errorf("логин %q уже занят другим пользователем — задайте ADMIN_USERNAME", b.username)
	}
	if err != nil {
		return err
	}
	if generated {
		b.save(user.Username, password)
	}
	// При создании пароль виден всегда, кроме случая, когда его задали в окружении сами (и это не DEV_MODE).
	announce("Создан супер-админ", user.Username, password, source, generated || b.dev)
	return nil
}

func (b adminBootstrap) resetPassword(ctx context.Context, store *db.Store, user db.User, title string) error {
	password, source, generated := b.initialPassword()
	hash, err := auth.HashPassword(password)
	if err != nil {
		return fmt.Errorf("первый пароль (%s): %w", source, err)
	}
	if err := store.SetPassword(ctx, user.ID, hash, generated); err != nil {
		return err
	}
	_ = store.SetUserStatus(ctx, user.ID, db.StatusActive, "")
	_, _ = store.RevokeUserSessions(ctx, user.ID, "password_reset", "")
	if generated {
		b.save(user.Username, password)
	}
	announce(title, user.Username, password, source, true)
	return nil
}

// save пишет пароль в ADMIN_PASSWORD_FILE (права 0600). Ошибка не мешает запуску — пароль всё равно в логе.
func (b adminBootstrap) save(username, password string) {
	if b.file == "" {
		return
	}
	content := "# Still Awake: первый пароль супер-админа. Папка secrets/ в .gitignore — не коммитить.\n" +
		"username=" + username + "\npassword=" + password + "\n"
	if err := os.MkdirAll(filepath.Dir(b.file), 0o700); err != nil {
		log.Printf("ADMIN_PASSWORD_FILE: не удалось создать папку: %v", err)
		return
	}
	if err := os.WriteFile(b.file, []byte(content), 0o600); err != nil {
		log.Printf("ADMIN_PASSWORD_FILE: не удалось сохранить пароль: %v", err)
		return
	}
	log.Printf("Первый пароль супер-админа сохранён в %s", b.file)
}

// readPasswordFile читает строку password=… (или единственную непустую строку — если файл писали руками).
func readPasswordFile(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	var bare string
	s := bufio.NewScanner(f)
	for s.Scan() {
		line := strings.TrimSpace(s.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		if key, value, ok := strings.Cut(line, "="); ok {
			if strings.TrimSpace(key) == "password" {
				return strings.TrimSpace(value), nil
			}
			continue
		}
		if bare == "" {
			bare = line
		}
	}
	if err := s.Err(); err != nil {
		return "", err
	}
	if bare == "" {
		return "", fmt.Errorf("в %s нет строки password=…", path)
	}
	return bare, nil
}

func announce(title, username, password, source string, show bool) {
	shown := "(" + source + ")"
	if show {
		shown = password + "   (" + source + ")"
	}
	line := strings.Repeat("=", 64)
	log.Printf("\n%s\n%s\n  логин:  %s\n  пароль: %s\n%s", line, title, username, shown, line)
}

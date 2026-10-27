package main

import (
	"context"
	"os"
	"path/filepath"
	"testing"

	"still-awake-backend/internal/auth"
	"still-awake-backend/internal/db"
)

func TestSuperAdminPasswordFile(t *testing.T) {
	dir := t.TempDir()
	store, err := db.Open(filepath.Join(dir, "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	ctx := context.Background()
	if err := store.SeedAccessControl(ctx, auth.Permissions, auth.SystemRoles); err != nil {
		t.Fatal(err)
	}
	file := filepath.Join(dir, "secrets", "admin-password.txt")
	t.Setenv("ADMIN_PASSWORD_FILE", file)
	t.Setenv("DEV_MODE", "true")

	// Первый запуск: пароль сгенерирован и сохранён в файл.
	b, err := adminBootstrapFromEnv()
	if err != nil || b.password != "" {
		t.Fatalf("до создания пароля быть не должно: %v %q", err, b.password)
	}
	if err := ensureSuperAdmin(ctx, store, b); err != nil {
		t.Fatal(err)
	}
	saved, err := readPasswordFile(file)
	if err != nil || len(saved) < 12 {
		t.Fatalf("пароль не сохранён: %v %q", err, saved)
	}
	admin, _ := store.FirstUserWithRole(ctx, auth.RoleSuperAdmin)
	if admin.Username != "admin" || !auth.VerifyPassword(admin.PasswordHash, saved) || !admin.MustChangePassword {
		t.Fatalf("супер-админ: %+v", admin)
	}

	// Следующий запуск в DEV_MODE берёт тот же пароль из файла и ничего не меняет.
	b, _ = adminBootstrapFromEnv()
	if b.password != saved {
		t.Fatalf("пароль из файла: %q", b.password)
	}
	if err := ensureSuperAdmin(ctx, store, b); err != nil {
		t.Fatal(err)
	}
	again, _ := store.FirstUserWithRole(ctx, auth.RoleSuperAdmin)
	if again.PasswordHash != admin.PasswordHash {
		t.Fatal("DEV_MODE не должен менять пароль, если он известен")
	}

	// Файл потерян, пароль ещё не меняли — DEV_MODE выдаёт новый и снова сохраняет.
	_ = os.Remove(file)
	b, _ = adminBootstrapFromEnv()
	if err := ensureSuperAdmin(ctx, store, b); err != nil {
		t.Fatal(err)
	}
	fresh, err := readPasswordFile(file)
	reset, _ := store.FirstUserWithRole(ctx, auth.RoleSuperAdmin)
	if err != nil || fresh == saved || !auth.VerifyPassword(reset.PasswordHash, fresh) {
		t.Fatalf("новый пароль: %v %q", err, fresh)
	}

	// Пароль из окружения важнее файла.
	t.Setenv("ADMIN_INITIAL_PASSWORD", "from-env-password")
	if b, _ = adminBootstrapFromEnv(); b.password != "from-env-password" {
		t.Fatalf("ADMIN_INITIAL_PASSWORD: %q", b.password)
	}
}

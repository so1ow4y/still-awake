package main

import (
	"bufio"
	"log"
	"log/slog"
	"net/http"
	"os"
	"strconv"
	"strings"

	"still-awake-backend/internal/db"
	"still-awake-backend/internal/httpapi"
	"still-awake-backend/internal/llm"
)

func main() {
	// Local development convenience. Real environment variables still win.
	loadDotEnv(".env")

	addr := env("ADDR", "127.0.0.1:8080")
	store, err := db.Open(env("DB_PATH", "./data/still-awake.db"))
	if err != nil {
		log.Fatal(err)
	}
	defer store.Close()

	temp, _ := strconv.ParseFloat(env("LLM_TEMPERATURE", "0.8"), 64)
	cfg := httpapi.Config{
		AdminToken:     os.Getenv("ADMIN_TOKEN"),
		AnalyticsToken: os.Getenv("ANALYTICS_TOKEN"),
		DefaultProvider: llm.Config{
			Mode: env("LLM_MODE", "mock"), BaseURL: os.Getenv("LLM_BASE_URL"), Path: env("LLM_PATH", "/v1/chat/completions"),
			Model: os.Getenv("LLM_MODEL"), APIKey: os.Getenv("LLM_API_KEY"), Temperature: temp,
		},
		AllowClientProviderOverrides: envBool("ALLOW_CLIENT_PROVIDER_OVERRIDES", false),
		ProviderAllowlist:            splitCSV(os.Getenv("PROVIDER_ALLOWLIST")),
		AllowUnsafeProviderURLs:      envBool("ALLOW_UNSAFE_PROVIDER_URLS", false),
		AdminAllowSQLWrites:          envBool("ADMIN_ALLOW_SQL_WRITES", false),
	}
	api := httpapi.New(store, cfg, slog.Default())
	log.Printf("Still Awake backend listening on %s", addr)
	log.Printf("Debug/admin client: %s", displayURL(addr))
	log.Fatal(http.ListenAndServe(addr, api.Handler()))
}

func loadDotEnv(path string) {
	f, err := os.Open(path)
	if err != nil {
		return
	}
	defer f.Close()
	s := bufio.NewScanner(f)
	for s.Scan() {
		line := strings.TrimSpace(s.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		line = strings.TrimPrefix(line, "export ")
		key, value, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		key = strings.TrimSpace(key)
		value = strings.Trim(strings.TrimSpace(value), `"'`)
		if key == "" {
			continue
		}
		if _, exists := os.LookupEnv(key); !exists {
			_ = os.Setenv(key, value)
		}
	}
}

func displayURL(addr string) string {
	if strings.HasPrefix(addr, ":") {
		return "http://localhost" + addr + "/"
	}
	return "http://" + addr + "/"
}

func env(k, fallback string) string {
	if v := os.Getenv(k); v != "" {
		return v
	}
	return fallback
}
func envBool(k string, fallback bool) bool {
	v := os.Getenv(k)
	if v == "" {
		return fallback
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		return fallback
	}
	return b
}
func splitCSV(v string) []string {
	var out []string
	for _, x := range strings.Split(v, ",") {
		if x = strings.TrimSpace(x); x != "" {
			out = append(out, x)
		}
	}
	return out
}

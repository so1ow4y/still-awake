package llm

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestOpenAICompatible(t *testing.T) {
	fake := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if got := r.Header.Get("Authorization"); got != "Bearer secret" {
			t.Fatalf("unexpected auth: %q", got)
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{
          "choices":[{"message":{"role":"assistant","content":"still awake?"}}],
          "usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16},
          "stats":{"tokens_per_second":42.5,"time_to_first_token_seconds":0.2}
        }`))
	}))
	defer fake.Close()

	c := New()
	got, err := c.Chat(context.Background(), Config{Mode: "openai_compatible", BaseURL: fake.URL, Path: "/chat", Model: "fake", APIKey: "secret"}, []Message{{Role: "user", Content: "hello"}})
	if err != nil {
		t.Fatal(err)
	}
	if got.Text != "still awake?" {
		t.Fatalf("unexpected reply: %q", got.Text)
	}
	if got.Usage.TotalTokens != 16 || got.Performance.TokensPerSecond != 42.5 {
		t.Fatalf("unexpected metrics: %+v %+v", got.Usage, got.Performance)
	}
}

func TestListModels(t *testing.T) {
	fake := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/v1/models" {
			t.Fatalf("unexpected path: %s", r.URL.Path)
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"data":[{"id":"qwen3-4b-2507","object":"model"}]}`))
	}))
	defer fake.Close()

	models, err := New().ListModels(context.Background(), Config{Mode: "openai_compatible", BaseURL: fake.URL})
	if err != nil {
		t.Fatal(err)
	}
	if len(models) != 1 || models[0].ID != "qwen3-4b-2507" {
		t.Fatalf("unexpected models: %+v", models)
	}
}

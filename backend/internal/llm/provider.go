package llm

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

type Message struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

type Config struct {
	Mode        string  `json:"mode"`
	BaseURL     string  `json:"base_url,omitempty"`
	Path        string  `json:"path,omitempty"`
	Model       string  `json:"model,omitempty"`
	APIKey      string  `json:"api_key,omitempty"`
	Temperature float64 `json:"temperature,omitempty"`
}

type Usage struct {
	PromptTokens     int `json:"prompt_tokens"`
	CompletionTokens int `json:"completion_tokens"`
	TotalTokens      int `json:"total_tokens"`
}

type Performance struct {
	TokensPerSecond         float64 `json:"tokens_per_second,omitempty"`
	TimeToFirstTokenSeconds float64 `json:"time_to_first_token_seconds,omitempty"`
}

type Result struct {
	Text        string      `json:"text"`
	Provider    string      `json:"provider"`
	Model       string      `json:"model,omitempty"`
	Usage       Usage       `json:"usage"`
	Performance Performance `json:"performance"`
	LatencyMS   int64       `json:"latency_ms"`
}

type ModelInfo struct {
	ID      string `json:"id"`
	Object  string `json:"object,omitempty"`
	OwnedBy string `json:"owned_by,omitempty"`
}

type ProviderStatus struct {
	Online           bool                 `json:"online"`
	BaseURL          string               `json:"base_url"`
	SelectedModel    string               `json:"selected_model,omitempty"`
	ModelFound       bool                 `json:"model_found"`
	ModelLoaded      *bool                `json:"model_loaded,omitempty"`
	ContextLength    int                  `json:"context_length,omitempty"`
	MaxContextLength int                  `json:"max_context_length,omitempty"`
	LoadedInstanceID string               `json:"loaded_instance_id,omitempty"`
	Models           []ProviderModelState `json:"models,omitempty"`
	Note             string               `json:"note,omitempty"`
}

type ProviderModelState struct {
	Key              string `json:"key"`
	DisplayName      string `json:"display_name,omitempty"`
	Type             string `json:"type,omitempty"`
	Loaded           bool   `json:"loaded"`
	ContextLength    int    `json:"context_length,omitempty"`
	MaxContextLength int    `json:"max_context_length,omitempty"`
	InstanceID       string `json:"instance_id,omitempty"`
}

type Client struct{ HTTP *http.Client }

func New() *Client { return &Client{HTTP: &http.Client{Timeout: 120 * time.Second}} }

func (c *Client) Chat(ctx context.Context, cfg Config, messages []Message) (Result, error) {
	switch strings.ToLower(cfg.Mode) {
	case "", "mock":
		last := "..."
		for i := len(messages) - 1; i >= 0; i-- {
			if messages[i].Role == "user" {
				last = messages[i].Content
				break
			}
		}
		return Result{Text: fmt.Sprintf("[mock] Я видел твоё сообщение: %q", last), Provider: "mock", Model: "mock-stalker"}, nil
	case "openai_compatible":
		return c.chatOpenAICompatible(ctx, cfg, messages)
	default:
		return Result{}, fmt.Errorf("unknown provider mode %q", cfg.Mode)
	}
}

func (c *Client) ListModels(ctx context.Context, cfg Config) ([]ModelInfo, error) {
	if strings.ToLower(cfg.Mode) == "mock" || cfg.Mode == "" {
		return []ModelInfo{{ID: "mock-stalker", Object: "model", OwnedBy: "still-awake"}}, nil
	}
	if strings.ToLower(cfg.Mode) != "openai_compatible" {
		return nil, fmt.Errorf("unknown provider mode %q", cfg.Mode)
	}
	if cfg.BaseURL == "" {
		return nil, errors.New("base_url is required")
	}
	u, err := url.Parse(strings.TrimRight(cfg.BaseURL, "/") + "/v1/models")
	if err != nil {
		return nil, err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, u.String(), nil)
	if err != nil {
		return nil, err
	}
	if cfg.APIKey != "" {
		req.Header.Set("Authorization", "Bearer "+cfg.APIKey)
	}
	resp, err := c.HTTP.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	b, _ := io.ReadAll(io.LimitReader(resp.Body, 4<<20))
	if resp.StatusCode/100 != 2 {
		return nil, fmt.Errorf("provider returned %s: %s", resp.Status, strings.TrimSpace(string(b)))
	}
	var decoded struct {
		Data []ModelInfo `json:"data"`
	}
	if err := json.Unmarshal(b, &decoded); err != nil {
		return nil, fmt.Errorf("decode provider models: %w", err)
	}
	return decoded.Data, nil
}

func (c *Client) chatOpenAICompatible(ctx context.Context, cfg Config, messages []Message) (Result, error) {
	if cfg.BaseURL == "" || cfg.Model == "" {
		return Result{}, errors.New("base_url and model are required")
	}
	if cfg.Path == "" {
		cfg.Path = "/v1/chat/completions"
	}
	u, err := url.Parse(strings.TrimRight(cfg.BaseURL, "/") + "/" + strings.TrimLeft(cfg.Path, "/"))
	if err != nil {
		return Result{}, err
	}
	payload := map[string]any{"model": cfg.Model, "messages": messages}
	if cfg.Temperature != 0 {
		payload["temperature"] = cfg.Temperature
	}
	body, _ := json.Marshal(payload)
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, u.String(), bytes.NewReader(body))
	if err != nil {
		return Result{}, err
	}
	req.Header.Set("Content-Type", "application/json")
	if cfg.APIKey != "" {
		req.Header.Set("Authorization", "Bearer "+cfg.APIKey)
	}
	started := time.Now()
	resp, err := c.HTTP.Do(req)
	latency := time.Since(started).Milliseconds()
	if err != nil {
		return Result{Provider: "openai_compatible", Model: cfg.Model, LatencyMS: latency}, err
	}
	defer resp.Body.Close()
	b, _ := io.ReadAll(io.LimitReader(resp.Body, 4<<20))
	if resp.StatusCode/100 != 2 {
		return Result{Provider: "openai_compatible", Model: cfg.Model, LatencyMS: latency}, fmt.Errorf("provider returned %s: %s", resp.Status, strings.TrimSpace(string(b)))
	}
	var decoded struct {
		Choices []struct {
			Message Message `json:"message"`
		} `json:"choices"`
		Usage Usage `json:"usage"`
		Stats struct {
			TokensPerSecond         float64 `json:"tokens_per_second"`
			TimeToFirstTokenSeconds float64 `json:"time_to_first_token_seconds"`
		} `json:"stats"`
	}
	if err := json.Unmarshal(b, &decoded); err != nil {
		return Result{Provider: "openai_compatible", Model: cfg.Model, LatencyMS: latency}, fmt.Errorf("decode provider response: %w", err)
	}
	if len(decoded.Choices) == 0 {
		return Result{Provider: "openai_compatible", Model: cfg.Model, LatencyMS: latency}, errors.New("provider returned no choices")
	}
	return Result{
		Text:     decoded.Choices[0].Message.Content,
		Provider: "openai_compatible",
		Model:    cfg.Model,
		Usage:    decoded.Usage,
		Performance: Performance{
			TokensPerSecond:         decoded.Stats.TokensPerSecond,
			TimeToFirstTokenSeconds: decoded.Stats.TimeToFirstTokenSeconds,
		},
		LatencyMS: latency,
	}, nil
}

func (c *Client) Status(ctx context.Context, cfg Config) (ProviderStatus, error) {
	st := ProviderStatus{BaseURL: cfg.BaseURL, SelectedModel: cfg.Model}
	if strings.ToLower(cfg.Mode) == "mock" || cfg.Mode == "" {
		loaded := true
		st.Online = true
		st.ModelFound = true
		st.ModelLoaded = &loaded
		st.Models = []ProviderModelState{{Key: "mock-stalker", DisplayName: "Mock", Type: "llm", Loaded: true}}
		return st, nil
	}
	if strings.ToLower(cfg.Mode) != "openai_compatible" {
		return st, fmt.Errorf("unknown provider mode %q", cfg.Mode)
	}
	if cfg.BaseURL == "" {
		return st, errors.New("base_url is required")
	}

	// Prefer LM Studio's native v1 endpoint because it tells us which downloaded
	// models are actually loaded and exposes their effective context length.
	nativeURL := strings.TrimRight(cfg.BaseURL, "/") + "/api/v1/models"
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, nativeURL, nil)
	if err != nil {
		return st, err
	}
	if cfg.APIKey != "" {
		req.Header.Set("Authorization", "Bearer "+cfg.APIKey)
	}
	resp, err := c.HTTP.Do(req)
	if err == nil {
		defer resp.Body.Close()
		b, _ := io.ReadAll(io.LimitReader(resp.Body, 4<<20))
		if resp.StatusCode/100 == 2 {
			var decoded struct {
				Models []struct {
					Key              string `json:"key"`
					DisplayName      string `json:"display_name"`
					Type             string `json:"type"`
					MaxContextLength int    `json:"max_context_length"`
					LoadedInstances  []struct {
						ID     string `json:"id"`
						Config struct {
							ContextLength int `json:"context_length"`
						} `json:"config"`
					} `json:"loaded_instances"`
				} `json:"models"`
			}
			if err := json.Unmarshal(b, &decoded); err == nil {
				st.Online = true
				loadedSelected := false
				for _, m := range decoded.Models {
					state := ProviderModelState{Key: m.Key, DisplayName: m.DisplayName, Type: m.Type, MaxContextLength: m.MaxContextLength}
					if len(m.LoadedInstances) > 0 {
						state.Loaded = true
						state.InstanceID = m.LoadedInstances[0].ID
						state.ContextLength = m.LoadedInstances[0].Config.ContextLength
					}
					st.Models = append(st.Models, state)
					if cfg.Model != "" && (m.Key == cfg.Model || state.InstanceID == cfg.Model) {
						st.ModelFound = true
						loadedSelected = state.Loaded
						st.ContextLength = state.ContextLength
						st.MaxContextLength = state.MaxContextLength
						st.LoadedInstanceID = state.InstanceID
					}
				}
				if cfg.Model == "" {
					st.ModelFound = len(st.Models) > 0
				}
				st.ModelLoaded = &loadedSelected
				return st, nil
			}
		}
	}

	// Fallback for older/non-LM-Studio OpenAI-compatible servers. This confirms
	// reachability and model availability, but loaded/unloaded state is unknown.
	models, fallbackErr := c.ListModels(ctx, cfg)
	if fallbackErr != nil {
		if err != nil {
			return st, err
		}
		return st, fallbackErr
	}
	st.Online = true
	st.Note = "Provider отвечает через /v1/models; loaded state недоступен."
	for _, m := range models {
		st.Models = append(st.Models, ProviderModelState{Key: m.ID, DisplayName: m.ID})
		if cfg.Model == "" || m.ID == cfg.Model {
			st.ModelFound = true
		}
	}
	return st, nil
}

package frontend

import (
	"embed"
	"io/fs"
	"net/http"
)

//go:embed web/*
var assets embed.FS

// Handler serves the responsive debug/admin frontend. Keeping it in a
// dedicated package lets the backend/API and the browser UI evolve separately
// while still shipping as a single dev executable.
func Handler() http.Handler {
	sub, err := fs.Sub(assets, "web")
	if err != nil {
		panic(err)
	}
	return http.FileServer(http.FS(sub))
}

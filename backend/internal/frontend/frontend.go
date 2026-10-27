package frontend

import (
	"embed"
	"io/fs"
	"net/http"
	"path"
)

//go:embed web
var assets embed.FS

// Тип задаём сами: на Windows mime.TypeByExtension читает реестр, и там .js бывает text/plain —
// тогда браузер отказывается грузить ES-модули панели.
var contentTypes = map[string]string{
	".html": "text/html; charset=utf-8",
	".js":   "text/javascript; charset=utf-8",
	".mjs":  "text/javascript; charset=utf-8",
	".css":  "text/css; charset=utf-8",
	".svg":  "image/svg+xml",
	".json": "application/json; charset=utf-8",
	".md":   "text/markdown; charset=utf-8",
}

// Handler отдаёт панель управления (Preact + htm без сборки). Она лежит в отдельном пакете, чтобы API и
// интерфейс развивались независимо, но поставляются одним бинарником. dir — папка с файлами панели на диске
// (FRONTEND_DIR): тогда правки видны сразу, без пересборки сервера.
func Handler(dir string) http.Handler {
	var files http.Handler
	if dir != "" {
		files = http.FileServer(http.Dir(dir))
	} else {
		sub, err := fs.Sub(assets, "web")
		if err != nil {
			panic(err)
		}
		files = http.FileServer(http.FS(sub))
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		ext := path.Ext(r.URL.Path)
		if r.URL.Path == "/" {
			ext = ".html"
		}
		if ct, ok := contentTypes[ext]; ok {
			w.Header().Set("Content-Type", ct)
		}
		// Панель обновляется вместе с сервером — браузер должен перепроверять файлы.
		w.Header().Set("Cache-Control", "no-cache")
		files.ServeHTTP(w, r)
	})
}

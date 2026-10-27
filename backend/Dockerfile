# Still Awake backend: статический бинарник Go (SQLite без CGO) в маленьком образе.
# Сборка и запуск — docker compose up -d --build (см. docker-compose.yml и README).

FROM golang:1.23-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /out/still-awake ./cmd/server

FROM alpine:3.20
RUN apk add --no-cache ca-certificates tzdata \
    && adduser -D -H -u 10001 app \
    && mkdir -p /app/data /app/secrets \
    && chown -R app:app /app
WORKDIR /app
COPY --from=build /out/still-awake /app/still-awake

# В контейнере слушаем все интерфейсы; наружу порт публикует compose.
ENV ADDR=0.0.0.0:8080 \
    DB_PATH=/app/data/still-awake.db \
    ADMIN_PASSWORD_FILE=/app/secrets/admin-password.txt

USER app
EXPOSE 8080
VOLUME ["/app/data", "/app/secrets"]
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget -qO- http://127.0.0.1:8080/healthz >/dev/null || exit 1
ENTRYPOINT ["/app/still-awake"]

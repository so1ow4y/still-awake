// Package auth: пароли, токены сессий и каталог прав и системных ролей.
package auth

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"fmt"
	"math/big"
	"strconv"
	"strings"
)

// PBKDF2-HMAC-SHA256 из стандартной библиотеки: без внешних зависимостей, параметры зашиты в сам хеш,
// поэтому их можно усилить позже, не сбрасывая старые пароли.
const (
	hashScheme     = "pbkdf2-sha256"
	hashIterations = 210_000
	saltBytes      = 16
	keyBytes       = 32
	MinPasswordLen = 8
)

var ErrPasswordTooShort = fmt.Errorf("пароль — не короче %d символов", MinPasswordLen)

// HashPassword возвращает строку вида pbkdf2-sha256$итерации$соль$хеш (base64 без паддинга).
func HashPassword(password string) (string, error) {
	if len([]rune(password)) < MinPasswordLen {
		return "", ErrPasswordTooShort
	}
	salt := make([]byte, saltBytes)
	if _, err := rand.Read(salt); err != nil {
		return "", err
	}
	key := pbkdf2SHA256([]byte(password), salt, hashIterations, keyBytes)
	enc := base64.RawStdEncoding
	return fmt.Sprintf("%s$%d$%s$%s", hashScheme, hashIterations, enc.EncodeToString(salt), enc.EncodeToString(key)), nil
}

// VerifyPassword сравнивает за постоянное время. Пустой хеш (аккаунт без пароля) не подходит никогда.
func VerifyPassword(encoded, password string) bool {
	parts := strings.Split(encoded, "$")
	if len(parts) != 4 || parts[0] != hashScheme {
		return false
	}
	iterations, err := strconv.Atoi(parts[1])
	if err != nil || iterations < 1 {
		return false
	}
	enc := base64.RawStdEncoding
	salt, err1 := enc.DecodeString(parts[2])
	want, err2 := enc.DecodeString(parts[3])
	if err1 != nil || err2 != nil || len(want) == 0 {
		return false
	}
	got := pbkdf2SHA256([]byte(password), salt, iterations, len(want))
	return subtle.ConstantTimeCompare(got, want) == 1
}

// pbkdf2SHA256 — RFC 8018, PRF = HMAC-SHA256.
func pbkdf2SHA256(password, salt []byte, iterations, keyLen int) []byte {
	prf := hmac.New(sha256.New, password)
	hashLen := prf.Size()
	blocks := (keyLen + hashLen - 1) / hashLen
	out := make([]byte, 0, blocks*hashLen)
	u := make([]byte, hashLen)
	for block := 1; block <= blocks; block++ {
		prf.Reset()
		prf.Write(salt)
		prf.Write([]byte{byte(block >> 24), byte(block >> 16), byte(block >> 8), byte(block)})
		out = prf.Sum(out)
		t := out[len(out)-hashLen:]
		copy(u, t)
		for i := 2; i <= iterations; i++ {
			prf.Reset()
			prf.Write(u)
			u = prf.Sum(u[:0])
			for x := range u {
				t[x] ^= u[x]
			}
		}
	}
	return out[:keyLen]
}

// NewToken — случайный токен сессии (32 байта, hex). В БД хранится только TokenHash.
func NewToken() string {
	b := make([]byte, 32)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return hex.EncodeToString(b)
}

func TokenHash(token string) string {
	sum := sha256.Sum256([]byte(token))
	return hex.EncodeToString(sum[:])
}

// NewID — идентификатор записи с префиксом: usr_…, role_…, ses_….
func NewID(prefix string) string {
	b := make([]byte, 12)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return prefix + "_" + hex.EncodeToString(b)
}

// Без похожих символов (0/O, 1/l/I), чтобы пароль можно было продиктовать или переписать с экрана.
const passwordAlphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789"

// RandomPassword — пароль для нового аккаунта или сброса.
func RandomPassword(length int) string {
	if length < MinPasswordLen {
		length = MinPasswordLen
	}
	var sb strings.Builder
	max := big.NewInt(int64(len(passwordAlphabet)))
	for i := 0; i < length; i++ {
		n, err := rand.Int(rand.Reader, max)
		if err != nil {
			panic(err)
		}
		sb.WriteByte(passwordAlphabet[n.Int64()])
	}
	return sb.String()
}

var errBadUsername = errors.New("логин: 3–32 символа — латиница, цифры, точка, дефис или подчёркивание, начинается с буквы")

// NormalizeUsername проверяет логин сотрудника. Регистр сохраняется, уникальность — без учёта регистра (в БД).
func NormalizeUsername(v string) (string, error) {
	v = strings.TrimSpace(v)
	if len(v) < 3 || len(v) > 32 {
		return "", errBadUsername
	}
	for i, r := range v {
		letter := (r >= 'a' && r <= 'z') || (r >= 'A' && r <= 'Z')
		if i == 0 && !letter {
			return "", errBadUsername
		}
		if !letter && !(r >= '0' && r <= '9') && r != '_' && r != '.' && r != '-' {
			return "", errBadUsername
		}
	}
	return v, nil
}

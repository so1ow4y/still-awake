package auth

import (
	"encoding/hex"
	"testing"
)

// Векторы PBKDF2-HMAC-SHA256 из RFC 7914, раздел 11.
func TestPBKDF2Vectors(t *testing.T) {
	cases := []struct {
		password, salt string
		iterations     int
		keyLen         int
		want           string
	}{
		{"passwd", "salt", 1, 64, "55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc49ca9cccf179b645991664b39d77ef317c71b845b1e30bd509112041d3a19783"},
		{"Password", "NaCl", 80000, 64, "4ddcd8f60b98be21830cee5ef22701f9641a4418d04c0414aeff08876b34ab56a1d425a1225833549adb841b51c9b3176a272bdebba1d078478f62b397f33c8d"},
	}
	for _, c := range cases {
		got := hex.EncodeToString(pbkdf2SHA256([]byte(c.password), []byte(c.salt), c.iterations, c.keyLen))
		if got != c.want {
			t.Errorf("pbkdf2(%q, %q, %d) = %s, want %s", c.password, c.salt, c.iterations, got, c.want)
		}
	}
}

func TestHashAndVerify(t *testing.T) {
	h, err := HashPassword("correct horse")
	if err != nil {
		t.Fatal(err)
	}
	if !VerifyPassword(h, "correct horse") {
		t.Fatal("правильный пароль не прошёл")
	}
	if VerifyPassword(h, "correct horsE") || VerifyPassword("", "x") || VerifyPassword("garbage", "x") {
		t.Fatal("неправильный пароль или хеш прошёл")
	}
	if _, err := HashPassword("short"); err == nil {
		t.Fatal("короткий пароль принят")
	}
}

func TestNormalize(t *testing.T) {
	phones := map[string]string{
		"+7 (900) 123-45-67": "+79001234567",
		"8 900 123 45 67":    "+79001234567",
		"+44 20 7946 0958":   "+442079460958",
	}
	for in, want := range phones {
		got, err := NormalizePhone(in)
		if err != nil || got != want {
			t.Errorf("NormalizePhone(%q) = %q, %v; want %q", in, got, err, want)
		}
	}
	for _, bad := range []string{"12345", "+7 900 abc", "7+9001234567"} {
		if _, err := NormalizePhone(bad); err == nil {
			t.Errorf("NormalizePhone(%q) принял неверный номер", bad)
		}
	}
	for _, ok := range []string{"admin", "Ivan.Petrov", "a_b-c"} {
		if _, err := NormalizeUsername(ok); err != nil {
			t.Errorf("NormalizeUsername(%q): %v", ok, err)
		}
	}
	for _, bad := range []string{"ab", "1admin", "иван", "a b c", ""} {
		if _, err := NormalizeUsername(bad); err == nil {
			t.Errorf("NormalizeUsername(%q) принял неверный логин", bad)
		}
	}
}

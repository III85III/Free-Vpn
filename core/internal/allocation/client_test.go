package allocation

import (
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestCredentialPersistsAndRejectsDamage(t *testing.T) {
	dir := t.TempDir()
	a, err := Credential(dir)
	if err != nil {
		t.Fatal(err)
	}
	b, err := Credential(dir)
	if err != nil || a != b || len(a) != 64 {
		t.Fatal("credential changed")
	}
	if err := os.WriteFile(filepath.Join(dir, "allocation.key"), []byte("broken"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := Credential(dir); err == nil {
		t.Fatal("damaged credential replaced silently")
	}
}
func TestFetchAccount(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "POST" || r.Header.Get("Authorization") != "Bearer test-installation" {
			t.Error("invalid request")
		}
		w.Header().Set("Content-Type", "application/json")
		w.Write([]byte(`{"email":"test@example.org","refresh_token":"synthetic-token"}`))
	}))
	defer server.Close()
	a, err := Fetch(context.Background(), server.URL, "test-installation", server.Client())
	if err != nil || a.Email != "test@example.org" || a.RefreshToken != "synthetic-token" {
		t.Fatal("account not received")
	}
}
func TestFetchRejectsErrorsWithoutLeakingBody(t *testing.T) {
	for _, code := range []int{302, 401, 429, 503} {
		t.Run(http.StatusText(code), func(t *testing.T) {
			calls := 0
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				calls++
				w.Header().Set("Location", "/redirect")
				w.WriteHeader(code)
				w.Write([]byte("private-value"))
			}))
			defer server.Close()
			_, err := Fetch(context.Background(), server.URL, "test", server.Client())
			if err == nil || strings.Contains(err.Error(), "private-value") || calls != 1 {
				t.Fatal("unsafe error or redirect")
			}
		})
	}
}
func TestFetchRejectsInvalidAccount(t *testing.T) {
	for _, body := range []string{`{}`, `{"email":"a"}`, strings.Repeat("x", 32769)} {
		server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { w.Write([]byte(body)) }))
		_, err := Fetch(context.Background(), server.URL, "test", server.Client())
		server.Close()
		if err == nil {
			t.Fatal("invalid response accepted")
		}
	}
}

package service

import (
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/III85III/ONYX/core/internal/config"
	"github.com/III85III/ONYX/core/internal/controlplane"
)

type fixtureTransport struct{}

func (fixtureTransport) RoundTrip(*http.Request) (*http.Response, error) {
	return nil, http.ErrNotSupported
}

// The relay setting installs its transport and takes away only that one:
// a transport set by someone else (a test's fixture) stays when no relay
// is configured.
func TestAPIRelayKeepsOtherTransports(t *testing.T) {
	t.Cleanup(func() { controlplane.SetTransport(nil); apiRelayTransport = nil })
	fixture := fixtureTransport{}
	controlplane.SetTransport(fixture)

	if err := configureAPIRelay(""); err != nil {
		t.Fatal(err)
	}
	if controlplane.Transport() != (http.RoundTripper)(fixture) {
		t.Fatal("no relay must leave the fixture in place")
	}

	if err := configureAPIRelay("https://relay.example/?key=k"); err != nil {
		t.Fatal(err)
	}
	if controlplane.Transport() == (http.RoundTripper)(fixture) || controlplane.Transport() != apiRelayTransport {
		t.Fatal("the relay is installed")
	}
	if err := configureAPIRelay(""); err != nil {
		t.Fatal(err)
	}
	if controlplane.Transport() == apiRelayTransport && apiRelayTransport != nil {
		t.Fatal("clearing the relay takes it away")
	}
	if apiRelayTransport != nil {
		t.Fatal("no relay is recorded")
	}
	if err := configureAPIRelay("http://relay.example/"); err == nil {
		t.Fatal("a plain-http relay must be refused")
	}
}

// A request goes to the relay with the target in its url parameter, next
// to the relay's own key, with the method, headers, and body; the answer
// keeps the original request.
func TestRelayTransportForwardsToTarget(t *testing.T) {
	var got struct {
		target, key, method, header, body string
	}
	relay := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		got.target, got.key = r.URL.Query().Get("url"), r.URL.Query().Get("key")
		got.method, got.header = r.Method, r.Header.Get("Authorization")
		body, _ := io.ReadAll(r.Body)
		got.body = string(body)
		w.Header().Set("Set-Cookie", "challenge=1; Path=/")
		w.WriteHeader(http.StatusCreated)
		io.WriteString(w, "from the target")
	}))
	defer relay.Close()
	base, _ := url.Parse(relay.URL + "/relay.php?key=secret")
	rt := &relayTransport{relay: base, base: relay.Client().Transport}

	req, _ := http.NewRequest(http.MethodPost, "https://vpn.mozilla.org/api/v1/fpn/token?a=1&b=2", strings.NewReader(`{"x":1}`))
	req.Header.Set("Authorization", "Bearer test")
	resp, err := rt.RoundTrip(req)
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	if got.target != "https://vpn.mozilla.org/api/v1/fpn/token?a=1&b=2" || got.key != "secret" {
		t.Fatalf("relay got target %q key %q", got.target, got.key)
	}
	if got.method != http.MethodPost || got.header != "Bearer test" || got.body != `{"x":1}` {
		t.Fatalf("relay got %+v", got)
	}
	if resp.StatusCode != http.StatusCreated || string(body) != "from the target" {
		t.Fatalf("answer %d %q", resp.StatusCode, body)
	}
	if resp.Request != req {
		t.Fatal("the answer must keep the original request, for cookies and redirects")
	}
}

// A relay that refuses (X-Relay-Error) is an error that names the relay.
func TestRelayTransportReportsRefusal(t *testing.T) {
	relay := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Relay-Error", "host not allowed")
		w.WriteHeader(http.StatusForbidden)
	}))
	defer relay.Close()
	base, _ := url.Parse(relay.URL)
	rt := &relayTransport{relay: base, base: relay.Client().Transport}
	req, _ := http.NewRequest(http.MethodGet, "https://example.com/", nil)
	_, err := rt.RoundTrip(req)
	if err == nil || !strings.Contains(err.Error(), "host not allowed") || !strings.Contains(err.Error(), base.Host) {
		t.Fatalf("got %v", err)
	}
}

func TestValidateRelay(t *testing.T) {
	for raw, ok := range map[string]bool{
		"":                                    true,
		"https://relay.example.workers.dev":   true,
		"https://example.com/relay.php?key=k": true,
		"http://example.com/relay.php":        false,
		"relay.example":                       false,
	} {
		if err := config.ValidateRelay(raw); (err == nil) != ok {
			t.Errorf("%q: %v", raw, err)
		}
	}
}

func TestRelayTransportAutomaticFallback(t *testing.T) {
	// Dead server simulating an expired/unreachable domain
	deadServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {}))
	deadServer.Close()
	deadURL, _ := url.Parse(deadServer.URL)

	var fallbackCalled bool
	goodServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		fallbackCalled = true
		w.WriteHeader(http.StatusOK)
		io.WriteString(w, "fallback ok")
	}))
	defer goodServer.Close()
	goodURL, _ := url.Parse(goodServer.URL)

	rt := &relayTransport{
		relay:     deadURL,
		fallbacks: []*url.URL{goodURL},
		base:      goodServer.Client().Transport,
	}

	req, _ := http.NewRequest(http.MethodGet, "https://vpn.mozilla.org/api/v1/servers", nil)
	resp, err := rt.RoundTrip(req)
	if err != nil {
		t.Fatalf("expected fallback to succeed, got %v", err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	if !fallbackCalled || string(body) != "fallback ok" {
		t.Fatalf("expected fallback called, got %q", body)
	}
}

func TestRelayHeaderTimeoutDoesNotReplayTokenPost(t *testing.T) {
	slow := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		<-r.Context().Done()
	}))
	defer slow.Close()
	primary, _ := url.Parse(slow.URL)
	backup := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			t.Error("ambiguous token POST was replayed")
		}
		w.WriteHeader(http.StatusNoContent)
	}))
	defer backup.Close()
	fallback, _ := url.Parse(backup.URL)
	rt := &relayTransport{relay: primary, fallbacks: []*url.URL{fallback},
		base: &http.Transport{ResponseHeaderTimeout: 75 * time.Millisecond}}
	for _, method := range []string{http.MethodGet, http.MethodPost} {
		rt.skipPrimaryUntil.Store(0)
		req, _ := http.NewRequest(method, "https://vpn.mozilla.org/api/v1/fpn/token", nil)
		resp, err := rt.RoundTrip(req)
		if resp != nil {
			resp.Body.Close()
		}
		if method == http.MethodGet && (err != nil || resp == nil || resp.StatusCode != http.StatusNoContent) {
			t.Fatalf("GET did not reach fallback: %v", err)
		}
		if method == http.MethodPost && err == nil {
			t.Fatal("timed-out POST should return its error without a replay")
		}
		if rt.skipPrimaryUntil.Load() <= time.Now().UnixNano() {
			t.Fatal("slow primary should be skipped by the next request")
		}
	}
}

func TestRelayFallbackDirectAndRelay(t *testing.T) {
	var directCalls, relayCalls int
	var directStatusCode = http.StatusOK

	directServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		directCalls++
		w.WriteHeader(directStatusCode)
		io.WriteString(w, "direct response")
	}))
	defer directServer.Close()

	relayServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		relayCalls++
		w.WriteHeader(http.StatusOK)
		io.WriteString(w, "relay response")
	}))
	defer relayServer.Close()

	directURL, _ := url.Parse(directServer.URL)
	relayURL, _ := url.Parse(relayServer.URL)

	fb := &relayFallback{
		direct: &http.Transport{},
		relay: &relayTransport{
			relay: relayURL,
			base:  relayServer.Client().Transport,
		},
	}

	// 1. Direct success
	req, _ := http.NewRequest(http.MethodGet, directURL.String(), nil)
	resp, err := fb.RoundTrip(req)
	if err != nil || resp.StatusCode != http.StatusOK {
		t.Fatalf("direct should succeed: %v", err)
	}
	body, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if string(body) != "direct response" || directCalls != 1 || relayCalls != 0 {
		t.Fatalf("expected direct response, got %s (direct=%d, relay=%d)", body, directCalls, relayCalls)
	}

	// 2. Direct returns 451 -> falls back to relay
	directStatusCode = http.StatusUnavailableForLegalReasons
	fb.relayUntil = time.Time{} // reset cooldown
	fb.lastHost = ""

	resp, err = fb.RoundTrip(req)
	if err != nil || resp.StatusCode != http.StatusOK {
		t.Fatalf("relay fallback should succeed: %v", err)
	}
	body, _ = io.ReadAll(resp.Body)
	resp.Body.Close()
	if string(body) != "relay response" || directCalls != 2 || relayCalls != 1 {
		t.Fatalf("expected relay response, got %s (direct=%d, relay=%d)", body, directCalls, relayCalls)
	}

	// 3. Since host recently failed, next request uses relay first
	resp, err = fb.RoundTrip(req)
	if err != nil || resp.StatusCode != http.StatusOK {
		t.Fatalf("relay should be used directly: %v", err)
	}
	resp.Body.Close()
	if directCalls != 2 || relayCalls != 2 {
		t.Fatalf("expected relay called directly without direct attempt, directCalls=%d, relayCalls=%d", directCalls, relayCalls)
	}
}

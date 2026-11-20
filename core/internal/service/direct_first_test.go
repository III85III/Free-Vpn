package service

import (
	"errors"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"
	"testing"
	"time"
)

type directFirstFixture func(*http.Request) (*http.Response, error)

func TestDirectNetworkFailureUsesRelayOnce(t *testing.T) {
	calls := 0
	f := &relayFallback{
		direct: directFirstFixture(func(*http.Request) (*http.Response, error) { return nil, errors.New("connection refused") }),
		relay:  directFirstFixture(func(*http.Request) (*http.Response, error) { calls++; return nil, errors.New("relay unavailable") }),
	}
	req, _ := http.NewRequest("GET", "https://vpn.mozilla.org/api/v1/test", nil)
	if _, err := f.RoundTrip(req); err == nil || calls != 1 {
		t.Fatal("expected exactly one relay attempt")
	}
}

func (fn directFirstFixture) RoundTrip(req *http.Request) (*http.Response, error) { return fn(req) }

func TestDirectFirstOnlyRelaysRegionDenial(t *testing.T) {
	for _, code := range []int{200, 401, 403, 406, 429, 451, 502, 503, 504} {
		directCalls, relayCalls := 0, 0
		f := &relayFallback{
			direct: directFirstFixture(func(*http.Request) (*http.Response, error) {
				directCalls++
				return &http.Response{StatusCode: code, Body: io.NopCloser(strings.NewReader(""))}, nil
			}),
			relay: directFirstFixture(func(*http.Request) (*http.Response, error) {
				relayCalls++
				return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(""))}, nil
			}),
		}
		call := func() {
			req, _ := http.NewRequest("GET", "https://vpn.mozilla.org/api/v1/fpn/token", nil)
			resp, err := f.RoundTrip(req)
			if err != nil {
				t.Fatal(err)
			}
			resp.Body.Close()
		}
		call()
		if (relayCalls == 1) != (code == 451 || code == 502 || code == 503 || code == 504) {
			t.Fatalf("unexpected fallback for %d", code)
		}
		if code == 451 {
			call()
			if directCalls != 1 || relayCalls != 2 {
				t.Fatal("region denial was not remembered")
			}
			f.mu.Lock()
			f.relayUntil = time.Now().Add(-time.Second)
			f.mu.Unlock()
			call()
			if directCalls != 2 {
				t.Fatal("direct route was not retried after cooldown")
			}
		}
	}
}

func TestDirectTimeoutDoesNotReplayTokenRequest(t *testing.T) {
	failure := errors.New("direct timeout")
	f := &relayFallback{
		direct: directFirstFixture(func(*http.Request) (*http.Response, error) { return nil, failure }),
		relay: directFirstFixture(func(*http.Request) (*http.Response, error) {
			t.Fatal("must not replay an ambiguous token request")
			return nil, nil
		}),
	}
	req, _ := http.NewRequest("POST", "https://api.accounts.firefox.com/v1/oauth/token", strings.NewReader("{}"))
	if _, err := f.RoundTrip(req); !errors.Is(err, failure) {
		t.Fatal("original transport error was lost")
	}
}

func TestAccountTCPDialTimeoutUsesConfiguredRelayOnce(t *testing.T) {
	const target = "https://api.accounts.firefox.com/v1/oauth/token"
	primary, err := url.Parse("https://relay.example/route")
	if err != nil {
		t.Fatal(err)
	}
	fallback, err := url.Parse("https://backup.example/route")
	if err != nil {
		t.Fatal(err)
	}
	directCalls, relayCalls, fallbackCalls := 0, 0, 0
	relay := &relayTransport{
		relay:     primary,
		fallbacks: []*url.URL{fallback},
		base: directFirstFixture(func(r *http.Request) (*http.Response, error) {
			if r.URL.Host != primary.Host || r.URL.Query().Get("url") != target {
				fallbackCalls++
				return nil, errors.New("account request did not use the primary relay")
			}
			relayCalls++
			body, readErr := io.ReadAll(r.Body)
			if readErr != nil {
				return nil, readErr
			}
			if string(body) != `{"refresh":"test"}` {
				return nil, errors.New("account request body changed")
			}
			return &http.Response{StatusCode: http.StatusOK, Body: io.NopCloser(strings.NewReader("ok"))}, nil
		}),
	}
	f := &relayFallback{
		direct: directFirstFixture(func(*http.Request) (*http.Response, error) {
			directCalls++
			return nil, &net.OpError{Op: "dial", Net: "tcp", Err: errors.New("i/o timeout")}
		}),
		relay: relay,
	}
	req, _ := http.NewRequest(http.MethodPost, target, strings.NewReader(`{"refresh":"test"}`))
	resp, err := f.RoundTrip(req)
	if err != nil {
		t.Fatal(err)
	}
	resp.Body.Close()
	if directCalls != 1 || relayCalls != 1 || fallbackCalls != 0 {
		t.Fatalf("wanted one direct dial and one primary relay attempt; direct=%d relay=%d fallback=%d", directCalls, relayCalls, fallbackCalls)
	}
}

func TestAccountPOSTDoesNotRetryAmbiguousOrNonAccountFailures(t *testing.T) {
	for _, tc := range []struct {
		name string
		url  string
		err  error
	}{
		{name: "response read failed", url: "https://api.accounts.firefox.com/v1/oauth/token", err: &net.OpError{Op: "read", Net: "tcp", Err: errors.New("timeout")}},
		{name: "sign-in request dial failed", url: "https://api.accounts.firefox.com/v1/account/login", err: &net.OpError{Op: "dial", Net: "tcp", Err: errors.New("timeout")}},
		{name: "non-account dial failed", url: "https://vpn.mozilla.org/api/v1/test", err: &net.OpError{Op: "dial", Net: "tcp", Err: errors.New("timeout")}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			relayCalls := 0
			f := &relayFallback{
				direct: directFirstFixture(func(*http.Request) (*http.Response, error) { return nil, tc.err }),
				relay: directFirstFixture(func(*http.Request) (*http.Response, error) {
					relayCalls++
					return nil, errors.New("must not replay this POST")
				}),
			}
			req, _ := http.NewRequest(http.MethodPost, tc.url, strings.NewReader("test"))
			if _, err := f.RoundTrip(req); err == nil || relayCalls != 0 {
				t.Fatalf("unexpected retry: err=%v relayCalls=%d", err, relayCalls)
			}
		})
	}
}

func TestDirectAccountTransportIsScopedToMozillaAccounts(t *testing.T) {
	for _, host := range []string{"accounts.firefox.com", "api.accounts.firefox.com", "oauth.accounts.firefox.com", "vpn.mozilla.org", "accounts.firefox.com.example.org"} {
		t.Run(host, func(t *testing.T) {
			directCalls, accountCalls := 0, 0
			response := func() *http.Response {
				return &http.Response{StatusCode: http.StatusOK, Body: io.NopCloser(strings.NewReader(""))}
			}
			f := &relayFallback{
				direct:  directFirstFixture(func(*http.Request) (*http.Response, error) { directCalls++; return response(), nil }),
				account: directFirstFixture(func(*http.Request) (*http.Response, error) { accountCalls++; return response(), nil }),
				relay: directFirstFixture(func(*http.Request) (*http.Response, error) {
					t.Fatal("successful direct request must not use relay")
					return nil, nil
				}),
			}
			req, _ := http.NewRequest("POST", "https://"+host+"/v1/oauth/token", strings.NewReader("{}"))
			resp, err := f.RoundTrip(req)
			if err != nil {
				t.Fatal(err)
			}
			resp.Body.Close()
			wantAccount := host == "accounts.firefox.com" || host == "api.accounts.firefox.com" || host == "oauth.accounts.firefox.com"
			if (accountCalls == 1) != wantAccount || directCalls+accountCalls != 1 {
				t.Fatal("incorrect account transport selection")
			}
		})
	}
}

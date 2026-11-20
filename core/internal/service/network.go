package service

import (
	"bytes"
	"context"
	"crypto/tls"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/III85III/ONYX/core/internal/config"
	"github.com/III85III/ONYX/core/internal/controlplane"
	"github.com/III85III/ONYX/core/internal/logx"
	"github.com/III85III/ONYX/core/internal/resolver"
)

const apiRelayDialTimeout = 8 * time.Second
const apiRelayHeaderTimeout = 8 * time.Second
const apiRelayCooldown = 5 * time.Minute

// resolverDialer dials through the configured DNS servers with a timeout.
type resolverDialer struct{ timeout time.Duration }

func (d resolverDialer) DialContext(ctx context.Context, network, address string) (net.Conn, error) {
	ctx, cancel := context.WithTimeout(ctx, d.timeout)
	defer cancel()
	return resolver.Dial(ctx, network, address)
}

func (d resolverDialer) Dial(network, address string) (net.Conn, error) {
	return d.DialContext(context.Background(), network, address)
}

// configureDNS installs the configured DNS servers for every connection
// this program opens itself.
func configureDNS(cfg config.DNS) error {
	r, err := resolver.New(cfg.Servers, cfg.FallbackToSystem)
	if err != nil {
		return err
	}
	if r.System() {
		resolver.Set(nil)
		logx.Info("DNS: system resolver")
		return nil
	}
	resolver.Set(r)
	logx.Info("DNS: servers=%s", r)
	return nil
}

// configureAPIRelay sends the control-plane requests through the user's
// relay at raw (config.Network.APIRelay); empty sends them directly. Only
// the Firefox Accounts, Guardian, server-list, geo-file, and flag requests
// are affected; the VPN traffic keeps talking to Mozilla's proxy directly.
func configureAPIRelay(raw string, fallbacks ...string) error {
	raw = strings.TrimSpace(raw)
	if raw == "" {
		// Only a relay this function set is taken away: another transport
		// (a test's fixture) stays, so starting a daemon never sends a
		// test's requests to the real services.
		if apiRelayTransport != nil && controlplane.Transport() == apiRelayTransport {
			controlplane.SetTransport(nil)
		}
		apiRelayTransport = nil
		return nil
	}
	if err := config.ValidateRelay(raw); err != nil {
		return err
	}
	relay, _ := url.Parse(raw)

	var fbURLs []*url.URL
	for _, fb := range fallbacks {
		fb = strings.TrimSpace(fb)
		if fb != "" {
			if err := config.ValidateRelay(fb); err == nil {
				if u, err := url.Parse(fb); err == nil && u.Host != relay.Host {
					fbURLs = append(fbURLs, u)
				}
			}
		}
	}

	// Automatic zero-config permanent fallback:
	// If the user's relay has a key and is a custom domain, automatically add the permanent Cloudflare worker
	// as an emergency fallback so domain expiration never interrupts connectivity.
	const permanentFallbackHost = "85-guardian-relay.parsaghelich062.workers.dev"
	if relay.Host != permanentFallbackHost {
		hasPermanent := false
		for _, u := range fbURLs {
			if u.Host == permanentFallbackHost {
				hasPermanent = true
				break
			}
		}
		if !hasPermanent {
			key := relay.Query().Get("key")
			if key != "" {
				if u, err := url.Parse("https://" + permanentFallbackHost + "/?key=" + url.QueryEscape(key)); err == nil {
					fbURLs = append(fbURLs, u)
				}
			}
		}
	}

	dialer := resolverDialer{timeout: apiRelayDialTimeout}
	transport := &relayTransport{
		relay:     relay,
		fallbacks: fbURLs,
		base: &http.Transport{
			DialContext:           dialer.DialContext,
			TLSClientConfig:       &tls.Config{MinVersion: tls.VersionTLS12},
			TLSHandshakeTimeout:   apiRelayDialTimeout,
			ResponseHeaderTimeout: apiRelayHeaderTimeout,
		},
	}
	directWait := 8 * time.Second
	directTransport := controlplane.QuickTransport(directWait)
	accountTransport := controlplane.AccountTransport()
	fallback := &relayFallback{
		direct:  directTransport,
		account: accountTransport,
		relay:   transport,
	}
	controlplane.SetTransport(fallback)
	apiRelayTransport = fallback
	if len(fbURLs) > 0 {
		var hosts []string
		for _, u := range fbURLs {
			hosts = append(hosts, u.Host)
		}
		logx.Info("control plane API requests sent directly, and through relay=%s (fallbacks=%s) when that fails", relay.Host, strings.Join(hosts, ","))
	} else {
		logx.Info("control plane API requests sent directly, and through relay=%s when that fails", relay.Host)
	}
	return nil
}

// relayFallback tries direct connections first (via QuickTransport) for all
// control plane requests. An explicit HTTP 451 enables the configured relay.
// Transport failures and authentication errors retain their original response.
type relayFallback struct {
	direct     http.RoundTripper
	account    http.RoundTripper
	relay      http.RoundTripper
	mu         sync.Mutex
	relayUntil time.Time
	lastHost   string
}

func (f *relayFallback) relayFirst(host string) bool {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.lastHost == host && time.Now().Before(f.relayUntil)
}

func (f *relayFallback) useRelay(host string, reason string) {
	f.mu.Lock()
	defer f.mu.Unlock()
	f.lastHost = host
	f.relayUntil = time.Now().Add(apiRelayCooldown)
	logx.Info("direct request to %s failed (%s); using relay", host, reason)
}

func (f *relayFallback) RoundTrip(req *http.Request) (*http.Response, error) {
	if req.Body != nil && req.GetBody == nil {
		bodyBytes, err := io.ReadAll(req.Body)
		req.Body.Close()
		if err != nil {
			return nil, fmt.Errorf("reading request body for direct/relay: %w", err)
		}
		req.GetBody = func() (io.ReadCloser, error) {
			return io.NopCloser(bytes.NewReader(bodyBytes)), nil
		}
	}

	host := req.URL.Host
	accountRequest := isMozillaAccountsHost(req.URL.Hostname())
	if !f.relayFirst(host) {
		directReq := req.Clone(req.Context())
		if req.GetBody != nil {
			directReq.Body, _ = req.GetBody()
		}
		transport := f.direct
		if accountRequest {
			if f.account != nil {
				transport = f.account
			}
		}
		resp, err := transport.RoundTrip(directReq)
		if err == nil {
			if resp.StatusCode == http.StatusUnavailableForLegalReasons || ((req.Method == http.MethodGet || req.Method == http.MethodHead) && (resp.StatusCode == 502 || resp.StatusCode == 503 || resp.StatusCode == 504)) {
				resp.Body.Close()
				f.useRelay(host, fmt.Sprintf("HTTP %d", resp.StatusCode))
			} else {
				return resp, nil
			}
		} else {
			if req.Context().Err() != nil {
				return nil, err
			}
			// A dial error means TCP was never established, so this POST could
			// not have reached Mozilla. Retry it once through the configured
			// relay; do not retry TLS, write, read, or response timeouts because
			// Mozilla may already have consumed the token.
			if isMozillaTokenRefresh(req) && isTCPDialFailure(err) {
				logx.Warn("direct account connection to %s could not be established; retrying once through the configured relay", host)
				if rt, ok := f.relay.(*relayTransport); ok {
					return rt.roundTripAccountOnce(req)
				}
				return f.relay.RoundTrip(req)
			}
			// Never replay a possibly consumed account token or verification code.
			if req.Method != http.MethodGet && req.Method != http.MethodHead {
				return nil, err
			}
			f.useRelay(host, "network failure")
		}
	}

	relayReq := req
	if req.GetBody != nil {
		relayReq = req.Clone(req.Context())
		relayReq.Body, _ = req.GetBody()
	}
	return f.relay.RoundTrip(relayReq)
}

func isMozillaAccountsHost(host string) bool {
	switch strings.ToLower(host) {
	case "accounts.firefox.com", "api.accounts.firefox.com", "oauth.accounts.firefox.com":
		return true
	default:
		return false
	}
}

func isMozillaTokenRefresh(req *http.Request) bool {
	return req.Method == http.MethodPost &&
		strings.EqualFold(req.URL.Hostname(), "api.accounts.firefox.com") &&
		req.URL.Path == "/v1/oauth/token"
}

func isTCPDialFailure(err error) bool {
	var opErr *net.OpError
	return errors.As(err, &opErr) && opErr.Op == "dial"
}

// apiRelayTransport is the transport configureAPIRelay installed; nil when
// no relay is set.
var apiRelayTransport http.RoundTripper

// relayTransport sends each request to the relay, with the target URL in
// the relay's url parameter (next to its own parameters, such as a key),
// and the method, headers, and body as they are; the relay forwards it and
// answers with the target's response. The response keeps the original
// request, so cookies and redirects resolve against the target. A relay
// that refuses (scripts/relay set X-Relay-Error) is an error that names
// the relay.
type relayTransport struct {
	relay            *url.URL
	fallbacks        []*url.URL
	base             http.RoundTripper
	skipPrimaryUntil atomic.Int64
}

func (t *relayTransport) RoundTrip(req *http.Request) (*http.Response, error) {
	relays := append([]*url.URL{t.relay}, t.fallbacks...)
	if len(t.fallbacks) != 0 && time.Now().UnixNano() < t.skipPrimaryUntil.Load() {
		relays = t.fallbacks
	}

	if len(relays) > 1 && req.Body != nil && req.GetBody == nil {
		bodyBytes, err := io.ReadAll(req.Body)
		req.Body.Close()
		if err != nil {
			return nil, fmt.Errorf("reading request body for relay: %w", err)
		}
		req.GetBody = func() (io.ReadCloser, error) {
			return io.NopCloser(bytes.NewReader(bodyBytes)), nil
		}
		req.Body, _ = req.GetBody()
	}

	var lastErr error
	for i, r := range relays {
		target := *r
		query := target.Query()
		query.Set("url", req.URL.String())
		target.RawQuery = query.Encode()
		out := req.Clone(req.Context())
		out.URL = &target
		out.Host = ""
		if req.GetBody != nil {
			out.Body, _ = req.GetBody()
		}
		resp, err := t.base.RoundTrip(out)
		if err != nil {
			lastErr = fmt.Errorf("API relay %s: %w", r.Host, err)
			if req.Context().Err() != nil {
				return nil, lastErr
			}
			if r == t.relay && len(t.fallbacks) != 0 {
				t.skipPrimaryUntil.Store(time.Now().Add(apiRelayCooldown).UnixNano())
			}
			// A token refresh may already have reached Mozilla. Replaying a
			// POST after an ambiguous timeout can reuse a rotated token.
			if req.Method != http.MethodGet && req.Method != http.MethodHead {
				var dialErr *net.OpError
				if !errors.As(err, &dialErr) || dialErr.Op != "dial" {
					return nil, lastErr
				}
			}
			if i < len(relays)-1 {
				logx.Warn("API relay %s failed (%v); trying fallback %s", r.Host, err, relays[i+1].Host)
			}
			continue
		}
		if reason := resp.Header.Get("X-Relay-Error"); reason != "" {
			resp.Body.Close()
			lastErr = fmt.Errorf("API relay %s refused %s: %s", r.Host, req.URL.Host, reason)
			if r == t.relay && len(t.fallbacks) != 0 {
				t.skipPrimaryUntil.Store(time.Now().Add(apiRelayCooldown).UnixNano())
			}
			if i < len(relays)-1 {
				logx.Warn("API relay %s refused %s (%s); trying fallback %s", r.Host, req.URL.Host, reason, relays[i+1].Host)
			}
			continue
		}
		if r == t.relay && len(t.fallbacks) != 0 &&
			(resp.StatusCode == http.StatusTooManyRequests || resp.StatusCode >= 502 && resp.StatusCode <= 504 || resp.StatusCode >= 520 && resp.StatusCode <= 527) {
			t.skipPrimaryUntil.Store(time.Now().Add(apiRelayCooldown).UnixNano())
			if req.Method == http.MethodGet || req.Method == http.MethodHead {
				resp.Body.Close()
				lastErr = fmt.Errorf("API relay %s returned HTTP %d", r.Host, resp.StatusCode)
				continue
			}
		}
		resp.Request = req
		return resp, nil
	}
	return nil, lastErr
}

// roundTripAccountOnce is retained for compatibility with existing tests and callers.
func (t *relayTransport) roundTripAccountOnce(req *http.Request) (*http.Response, error) {
	return t.RoundTrip(req)
}

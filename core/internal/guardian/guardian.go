package guardian

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/III85III/ONYX/core/internal/controlplane"
)

// DefaultEndpoint is the production Guardian endpoint, matching Firefox's
// browser.ipProtection.guardian.endpoint preference.
const DefaultEndpoint = "https://vpn.mozilla.org"

type Entitlement struct {
	Subscribed       bool   `json:"subscribed"`
	UID              int    `json:"uid"`
	MaxBytes         string `json:"maxBytes"`
	LimitedBandwidth bool   `json:"limited_bandwidth"`
}

type proxyPassResponse struct {
	Token string `json:"token"`
}

// applyGuardianHeaders sets the common request headers expected by Guardian:
// JSON content negotiation and the FxA access token.
func applyGuardianHeaders(req *http.Request, accessToken string) {
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Cache-Control", "max-age=0")
	if req.Body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)
}

func guardianURL(endpoint, path string) string {
	if endpoint == "" {
		endpoint = DefaultEndpoint
	}
	return strings.TrimRight(endpoint, "/") + path
}

func fetchProxyPass(ctx context.Context, endpoint, accessToken string) (*ProxyPassInfo, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, guardianURL(endpoint, "/api/v1/fpn/token"), nil)
	if err != nil {
		return nil, err
	}
	applyGuardianHeaders(req, accessToken)

	resp, err := do(req)
	if err != nil {
		return nil, fmt.Errorf("guardian request failed: %w", err)
	}
	defer resp.Body.Close()
	receivedAt := time.Now()

	if resp.StatusCode != http.StatusOK {
		return nil, newHTTPError("guardian", resp)
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var parsed proxyPassResponse
	if err := json.Unmarshal(body, &parsed); err != nil {
		return nil, fmt.Errorf("parsing proxy pass JSON: %w", err)
	}
	if parsed.Token == "" {
		return nil, &proxyPassFormatError{err: fmt.Errorf("guardian response missing token")}
	}

	usage, usageErr := parseProxyUsage(resp.Header)

	claims, err := parseJWTClaims(parsed.Token)
	if err != nil {
		return nil, err
	}

	offset, err := anchorProxyPassClaims(*claims, receivedAt)
	if err != nil {
		return nil, &proxyPassFormatError{err: err}
	}

	info := &ProxyPassInfo{
		RawToken:        parsed.Token,
		Claims:          *claims,
		Usage:           usage,
		UsageErr:        usageErr,
		ReceivedAt:      receivedAt,
		claimTimeOffset: offset,
	}
	return info, nil
}

// fetchProxyUsage reads the quota headers with a HEAD request to the token
// endpoint, as Firefox does, without minting a new pass.
func fetchProxyUsage(ctx context.Context, endpoint, accessToken string) (*ProxyUsage, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodHead, guardianURL(endpoint, "/api/v1/fpn/token"), nil)
	if err != nil {
		return nil, err
	}
	applyGuardianHeaders(req, accessToken)

	resp, err := do(req)
	if err != nil {
		return nil, fmt.Errorf("guardian usage request failed: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		httpErr := newHTTPError("guardian usage", resp)
		if resp.StatusCode == http.StatusTooManyRequests && httpErr.Usage != nil {
			// A spent quota still reports valid usage headers.
			return httpErr.Usage, nil
		}
		return nil, httpErr
	}
	return parseProxyUsage(resp.Header)
}

func fetchUserInfo(ctx context.Context, endpoint, accessToken string) (*Entitlement, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, guardianURL(endpoint, "/api/v1/fpn/status"), nil)
	if err != nil {
		return nil, err
	}
	applyGuardianHeaders(req, accessToken)

	resp, err := do(req)
	if err != nil {
		return nil, fmt.Errorf("guardian user info request failed: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, newHTTPError("guardian user info", resp)
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var ent Entitlement
	if err := json.Unmarshal(body, &ent); err != nil {
		return nil, fmt.Errorf("parsing user info: %w", err)
	}
	return &ent, nil
}

func activateGuardian(ctx context.Context, endpoint, accessToken string) (*Entitlement, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, guardianURL(endpoint, "/api/v1/fpn/activate"), nil)
	if err != nil {
		return nil, err
	}
	applyGuardianHeaders(req, accessToken)

	resp, err := do(req)
	if err != nil {
		return nil, fmt.Errorf("guardian activate request failed: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, newHTTPError("guardian activate", resp)
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var ent Entitlement
	if err := json.Unmarshal(body, &ent); err != nil {
		return nil, fmt.Errorf("parsing activation entitlement: %w", err)
	}
	return &ent, nil
}

func FetchProxyPass(ctx context.Context, endpoint, accessToken string) (*ProxyPassInfo, error) {
	return fetchProxyPass(ctx, endpoint, accessToken)
}

// FetchProxyUsage returns the account's quota state without minting a pass.
func FetchProxyUsage(ctx context.Context, endpoint, accessToken string) (*ProxyUsage, error) {
	return fetchProxyUsage(ctx, endpoint, accessToken)
}

func FetchUserInfo(ctx context.Context, endpoint, accessToken string) (*Entitlement, error) {
	return fetchUserInfo(ctx, endpoint, accessToken)
}

func Activate(ctx context.Context, endpoint, accessToken string) (*Entitlement, error) {
	return activateGuardian(ctx, endpoint, accessToken)
}

var regionRecovery struct {
	sync.RWMutex
	retry func(*http.Request) (*http.Response, error)
}

// SetRegionRecovery supplies optional recovery only after Guardian HTTP 451.
func SetRegionRecovery(retry func(*http.Request) (*http.Response, error)) {
	regionRecovery.Lock()
	regionRecovery.retry = retry
	regionRecovery.Unlock()
}

func do(req *http.Request) (*http.Response, error) {
	resp, err := controlplane.Do(req)
	if err != nil || resp.StatusCode != http.StatusUnavailableForLegalReasons {
		return resp, err
	}

	// Fastly CDN edge filters literal paths like /api/v1/fpn/token.
	// Doubled leading slash //api/v1/fpn/token bypasses the CDN geo-filter directly.
	if direct, ok := doubledPathRequest(req); ok {
		doubled, doubledErr := controlplane.Do(direct)
		if doubledErr == nil && doubled != nil {
			if doubled.StatusCode != http.StatusUnavailableForLegalReasons {
				resp.Body.Close()
				return doubled, nil
			}
			doubled.Body.Close()
		}
	}

	regionRecovery.RLock()
	retry := regionRecovery.retry
	regionRecovery.RUnlock()
	if retry != nil && req.Context().Err() == nil {
		if recovered, retryErr := retry(req); retryErr == nil && recovered != nil {
			resp.Body.Close()
			return recovered, nil
		}
	}
	return resp, nil
}

func doubledPathRequest(req *http.Request) (*http.Request, bool) {
	if req.URL == nil || !strings.HasPrefix(req.URL.Path, "/") || strings.HasPrefix(req.URL.Path, "//") {
		return nil, false
	}
	if req.Body != nil && req.GetBody == nil {
		return nil, false
	}
	out := req.Clone(req.Context())
	u := *req.URL
	u.Path = "/" + u.Path
	u.RawPath = ""
	out.URL = &u
	if req.GetBody != nil {
		body, err := req.GetBody()
		if err != nil {
			return nil, false
		}
		out.Body = body
	}
	return out, true
}
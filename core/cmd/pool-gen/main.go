package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"math/big"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"

	"github.com/III85III/III85III-VPN/core/internal/controlplane"
	"github.com/III85III/III85III-VPN/core/internal/fxa"
)

type MailTmDomain struct {
	Domain string `json:"domain"`
}

type MailTmDomainsResponse struct {
	Members []MailTmDomain `json:"hydra:member"`
}

type MailTmMessageItem struct {
	ID    string `json:"id"`
	From  struct {
		Address string `json:"address"`
	} `json:"from"`
	Subject string `json:"subject"`
	Intro   string `json:"intro"`
}

type MailTmMessagesResponse struct {
	Members []MailTmMessageItem `json:"hydra:member"`
}

type MailTmMessageDetail struct {
	ID   string   `json:"id"`
	Text string   `json:"text"`
	HTML []string `json:"html"`
}

type PoolAccount struct {
	Email        string `json:"email"`
	RefreshToken string `json:"refresh_token"`
}

type AccountPool struct {
	UpdatedAt string        `json:"updated_at"`
	Accounts  []PoolAccount `json:"accounts"`
}

func randomString(n int) string {
	const letters = "abcdefghijklmnopqrstuvwxyz0123456789"
	b := make([]byte, n)
	for i := range b {
		num, _ := rand.Int(rand.Reader, big.NewInt(int64(len(letters))))
		b[i] = letters[num.Int64()]
	}
	return string(b)
}

func getMailTmDomain() (string, error) {
	req, err := http.NewRequest(http.MethodGet, "https://api.mail.tm/domains", nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	var d MailTmDomainsResponse
	if err := json.NewDecoder(resp.Body).Decode(&d); err != nil {
		return "", err
	}
	if len(d.Members) == 0 {
		return "", errors.New("no domains returned by mail.tm")
	}
	return d.Members[0].Domain, nil
}

func createMailTmAccount(address, password string) error {
	body, _ := json.Marshal(map[string]string{"address": address, "password": password})
	req, err := http.NewRequest(http.MethodPost, "https://api.mail.tm/accounts", bytes.NewReader(body))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK && resp.StatusCode != http.StatusCreated {
		data, _ := io.ReadAll(resp.Body)
		return fmt.Errorf("create mail.tm account failed (%d): %s", resp.StatusCode, string(data))
	}
	return nil
}

func loginMailTm(address, password string) (string, error) {
	body, _ := json.Marshal(map[string]string{"address": address, "password": password})
	req, err := http.NewRequest(http.MethodPost, "https://api.mail.tm/token", bytes.NewReader(body))
	if err != nil {
		return "", err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()
	var res struct {
		Token string `json:"token"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&res); err != nil {
		return "", err
	}
	if res.Token == "" {
		return "", errors.New("empty token from mail.tm")
	}
	return res.Token, nil
}

func extractVerificationInfo(text string) (code, uid string, isLink bool) {
	linkRe := regexp.MustCompile(`https://accounts\.firefox\.com/verify_email\?[^\s<"'>]+`)
	if link := linkRe.FindString(text); link != "" {
		if u, err := url.Parse(link); err == nil {
			q := u.Query()
			c, id := q.Get("code"), q.Get("uid")
			if c != "" && id != "" {
				return c, id, true
			}
		}
	}

	patterns := []*regexp.Regexp{
		regexp.MustCompile(`(?i)(?:code|firefox|authorization|confirm(?:ation)?|verification)[\s:]*([0-9]{6})`),
		regexp.MustCompile(`(?i)\b([0-9]{6})\b[\s]+is your`),
		regexp.MustCompile(`(?i)code\s+is\s+([0-9]{6})`),
	}
	for _, re := range patterns {
		if m := re.FindStringSubmatch(text); len(m) > 1 {
			return m[1], "", false
		}
	}
	return "", "", false
}

func waitForVerification(token string, timeout time.Duration) (code, uid string, isLink bool, err error) {
	deadline := time.Now().Add(timeout)

	for time.Now().Before(deadline) {
		req, _ := http.NewRequest(http.MethodGet, "https://api.mail.tm/messages", nil)
		req.Header.Set("Authorization", "Bearer "+token)
		req.Header.Set("User-Agent", "Mozilla/5.0")
		resp, err := http.DefaultClient.Do(req)
		if err == nil {
			var msgs MailTmMessagesResponse
			_ = json.NewDecoder(resp.Body).Decode(&msgs)
			resp.Body.Close()

			for _, m := range msgs.Members {
				log.Printf("Received email: Subject=%q From=%q", m.Subject, m.From.Address)
				if c, id, link := extractVerificationInfo(m.Subject); c != "" {
					return c, id, link, nil
				}

				detailReq, _ := http.NewRequest(http.MethodGet, "https://api.mail.tm/messages/"+m.ID, nil)
				detailReq.Header.Set("Authorization", "Bearer "+token)
				detailReq.Header.Set("User-Agent", "Mozilla/5.0")
				detailResp, err := http.DefaultClient.Do(detailReq)
				if err == nil {
					var detail MailTmMessageDetail
					_ = json.NewDecoder(detailResp.Body).Decode(&detail)
					detailResp.Body.Close()
					if c, id, link := extractVerificationInfo(detail.Text); c != "" {
						return c, id, link, nil
					}
					for _, h := range detail.HTML {
						if c, id, link := extractVerificationInfo(h); c != "" {
							return c, id, link, nil
						}
					}
				}
			}
		}
		time.Sleep(3 * time.Second)
	}
	return "", "", false, errors.New("timeout waiting for verification email from Mozilla")
}

func generateOneAccount(ctx context.Context) (*PoolAccount, error) {
	domain, err := getMailTmDomain()
	if err != nil {
		return nil, fmt.Errorf("domain: %w", err)
	}
	email := fmt.Sprintf("vpn_%s@%s", randomString(8), domain)
	password := fmt.Sprintf("VpnP@ss_%s", randomString(8))

	log.Printf("Creating temporary inbox: %s", email)
	if err := createMailTmAccount(email, password); err != nil {
		return nil, fmt.Errorf("mail.tm account: %w", err)
	}
	token, err := loginMailTm(email, password)
	if err != nil {
		return nil, fmt.Errorf("mail.tm login: %w", err)
	}

	log.Printf("Registering Firefox Account...")
	loginResp, err := fxa.CreateAccount(ctx, email, password)
	if err != nil {
		return nil, fmt.Errorf("fxa create: %w", err)
	}

	log.Printf("Waiting for Mozilla confirmation email...")
	verCode, verUid, isLink, err := waitForVerification(token, 40*time.Second)
	if err != nil {
		return nil, fmt.Errorf("getting confirmation: %w", err)
	}

	if isLink {
		log.Printf("Verifying account via recovery email link (uid=%s)...", verUid)
		if err := fxa.VerifyAccountEmail(ctx, verUid, verCode); err != nil {
			return nil, fmt.Errorf("verifying email link: %w", err)
		}
	} else {
		log.Printf("Verifying account via 6-digit code (%s)...", verCode)
		if err := fxa.VerifySession(ctx, loginResp.SessionToken, verCode); err != nil {
			return nil, fmt.Errorf("verifying session code: %w", err)
		}
	}

	log.Printf("Account verified! Issuing OAuth tokens...")
	tokResp, err := fxa.OAuthToken(ctx, loginResp.SessionToken)
	if err != nil {
		return nil, fmt.Errorf("oauth token: %w", err)
	}
	if tokResp.RefreshToken == "" {
		return nil, errors.New("no refresh token returned")
	}

	_ = fxa.DestroySession(ctx, loginResp.SessionToken)
	log.Printf("Successfully registered and provisioned 50 GB profile: %s", email)
	return &PoolAccount{
		Email:        email,
		RefreshToken: tokResp.RefreshToken,
	}, nil
}

func fetchProxies() []string {
	sources := []struct {
		url    string
		prefix string
	}{
		{"https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/http.txt", "http://"},
		{"https://raw.githubusercontent.com/TheSpeedX/PROXY-List/master/socks5.txt", "socks5://"},
		{"https://raw.githubusercontent.com/hookzof/socks5_list/master/proxy.txt", "socks5://"},
		{"https://raw.githubusercontent.com/roosterkid/openproxylist/main/HTTPS_RAW.txt", "http://"},
		{"https://raw.githubusercontent.com/roosterkid/openproxylist/main/SOCKS5_RAW.txt", "socks5://"},
	}
	var list []string
	client := &http.Client{Timeout: 6 * time.Second}
	for _, src := range sources {
		resp, err := client.Get(src.url)
		if err != nil {
			continue
		}
		data, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		lines := strings.Split(string(data), "\n")
		for _, l := range lines {
			l = strings.TrimSpace(l)
			if l != "" && !strings.HasPrefix(l, "#") {
				if !strings.HasPrefix(l, "http://") && !strings.HasPrefix(l, "socks5://") {
					list = append(list, src.prefix+l)
				} else {
					list = append(list, l)
				}
			}
		}
	}
	// Shuffle list using crypto/rand
	for i := len(list) - 1; i > 0; i-- {
		n, err := rand.Int(rand.Reader, big.NewInt(int64(i+1)))
		if err == nil {
			j := int(n.Int64())
			list[i], list[j] = list[j], list[i]
		}
	}
	return list
}

func testProxy(proxyStr string) (*http.Transport, error) {
	pu, err := url.Parse(proxyStr)
	if err != nil {
		return nil, err
	}
	tr := &http.Transport{
		Proxy: http.ProxyURL(pu),
	}
	testClient := &http.Client{
		Transport: tr,
		Timeout:   4 * time.Second,
	}

	// 1. Verify mail.tm
	resp, err := testClient.Get("https://api.mail.tm/domains")
	if err != nil {
		return nil, err
	}
	resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("mail.tm status: %d", resp.StatusCode)
	}

	// 2. Verify Firefox Accounts connectivity
	req, _ := http.NewRequest(http.MethodGet, "https://api.accounts.firefox.com/v1/client/websocket", nil)
	req.Header.Set("User-Agent", "Mozilla/5.0")
	resp2, err := testClient.Do(req)
	if err != nil {
		return nil, err
	}
	resp2.Body.Close()
	if resp2.StatusCode == http.StatusTooManyRequests || resp2.StatusCode == http.StatusForbidden {
		return nil, fmt.Errorf("fxa blocked: %d", resp2.StatusCode)
	}

	return tr, nil
}

func startProxyScanner(ctx context.Context, proxies []string, out chan<- *http.Transport) {
	jobs := make(chan string, 100)
	go func() {
		for {
			for _, p := range proxies {
				select {
				case <-ctx.Done():
					return
				case jobs <- p:
				}
			}
		}
	}()

	const workers = 50
	for w := 0; w < workers; w++ {
		go func() {
			for {
				select {
				case <-ctx.Done():
					return
				case p := <-jobs:
					tr, err := testProxy(p)
					if err == nil {
						select {
						case out <- tr:
						case <-ctx.Done():
							return
						}
					}
				}
			}
		}()
	}
}

func main() {
	count := flag.Int("count", 1, "Number of accounts to generate")
	poolPath := flag.String("pool", "dist/provision/pool.json", "Path to pool.json file")
	proxyFlag := flag.String("proxy", "", "Optional HTTP proxy (e.g. http://127.0.0.1:10810)")
	rotateFlag := flag.Bool("rotate-proxy", false, "Automatically rotate through free proxies")
	delaySec := flag.Int("delay", 2, "Delay in seconds between creations")
	flag.Parse()

	if *proxyFlag != "" {
		pu, err := url.Parse(*proxyFlag)
		if err != nil {
			log.Fatalf("invalid proxy URL: %v", err)
		}
		tr := &http.Transport{
			Proxy: http.ProxyURL(pu),
		}
		http.DefaultClient.Transport = tr
		controlplane.SetTransport(tr)
		log.Printf("Using proxy for pool generation: %s", *proxyFlag)
	}

	log.Printf("Account Pool Generator starting (target: %d accounts)...", *count)

	var proxyChan chan *http.Transport
	if *rotateFlag {
		log.Printf("Fetching public proxy list for IP rotation...")
		proxies := fetchProxies()
		log.Printf("Loaded %d proxies across HTTP and SOCKS5 lists. Starting parallel scanner...", len(proxies))
		proxyChan = make(chan *http.Transport, 15)
		startProxyScanner(context.Background(), proxies, proxyChan)
	}

	var pool AccountPool
	data, err := os.ReadFile(*poolPath)
	if err == nil {
		_ = json.Unmarshal(data, &pool)
	}
	existingEmails := make(map[string]bool)
	for _, acc := range pool.Accounts {
		existingEmails[acc.Email] = true
	}

	ctx := context.Background()
	generated := 0
	consecutiveFails := 0

	for generated < *count {
		log.Printf("[%d/%d] Generating account...", generated+1, *count)

		if *rotateFlag {
			log.Printf("Awaiting next verified proxy...")
			select {
			case tr := <-proxyChan:
				http.DefaultClient.Transport = tr
				controlplane.SetTransport(tr)
				controlplane.ResetJar()
				log.Printf("Connected through verified proxy node.")
			case <-time.After(30 * time.Second):
				log.Printf("Warning: proxy timeout, retrying proxy channel...")
				continue
			}
		}

		acc, err := generateOneAccount(ctx)
		if err != nil {
			consecutiveFails++
			log.Printf("Attempt failed (%d): %v", consecutiveFails, err)
			if consecutiveFails >= 25 {
				log.Printf("Too many consecutive failures (%d). Pausing 10s...", consecutiveFails)
				time.Sleep(10 * time.Second)
				consecutiveFails = 0
			}
			continue
		}

		consecutiveFails = 0
		if existingEmails[acc.Email] {
			log.Printf("Duplicate account %s, skipping...", acc.Email)
			continue
		}

		pool.Accounts = append(pool.Accounts, *acc)
		existingEmails[acc.Email] = true
		generated++

		pool.UpdatedAt = time.Now().UTC().Format(time.RFC3339)
		out, _ := json.MarshalIndent(pool, "", "  ")
		_ = os.MkdirAll(filepath.Dir(*poolPath), 0755)
		if err := os.WriteFile(*poolPath, out, 0644); err != nil {
			log.Printf("Error writing to %s: %v", *poolPath, err)
		} else {
			log.Printf("Saved pool to %s (pool total: %d accounts, %d GB)", *poolPath, len(pool.Accounts), len(pool.Accounts)*50)
		}

		// Sync to Android assets
		for _, rel := range []string{"../mobile/android/app/src/main/assets/accounts_pool.json", "../../mobile/android/app/src/main/assets/accounts_pool.json"} {
			assetPath := filepath.Join(filepath.Dir(*poolPath), rel)
			if _, err := os.Stat(filepath.Dir(assetPath)); err == nil {
				_ = os.WriteFile(assetPath, out, 0644)
			}
		}

		if generated < *count && *delaySec > 0 {
			time.Sleep(time.Duration(*delaySec) * time.Second)
		}
	}
	log.Printf("Done. Successfully generated %d/%d accounts. Total pool: %d accounts.", generated, *count, len(pool.Accounts))
}

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
	verCode, verUid, isLink, err := waitForVerification(token, 90*time.Second)
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

func main() {
	count := flag.Int("count", 1, "Number of accounts to generate")
	poolPath := flag.String("pool", "dist/provision/pool.json", "Path to pool.json file")
	proxyFlag := flag.String("proxy", "", "Optional HTTP proxy (e.g. http://127.0.0.1:10810)")
	delaySec := flag.Int("delay", 6, "Delay in seconds between creations")
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

	var pool AccountPool
	data, err := os.ReadFile(*poolPath)
	if err == nil {
		_ = json.Unmarshal(data, &pool)
	}

	ctx := context.Background()
	generated := 0
	for i := 0; i < *count; i++ {
		log.Printf("[%d/%d] Generating account...", i+1, *count)
		acc, err := generateOneAccount(ctx)
		if err != nil {
			log.Printf("Failed to generate account %d: %v", i+1, err)
			time.Sleep(10 * time.Second)
			continue
		}
		pool.Accounts = append(pool.Accounts, *acc)
		generated++

		pool.UpdatedAt = time.Now().UTC().Format(time.RFC3339)
		out, _ := json.MarshalIndent(pool, "", "  ")
		_ = os.MkdirAll(filepath.Dir(*poolPath), 0755)
		if err := os.WriteFile(*poolPath, out, 0644); err != nil {
			log.Printf("Error writing to %s: %v", *poolPath, err)
		} else {
			log.Printf("Saved pool to %s (total: %d accounts)", *poolPath, len(pool.Accounts))
		}

		// Also sync to Android assets if present
		assetPath := filepath.Join(filepath.Dir(*poolPath), "../mobile/android/app/src/main/assets/accounts_pool.json")
		if _, err := os.Stat(filepath.Dir(assetPath)); err == nil {
			_ = os.WriteFile(assetPath, out, 0644)
		}

		if i < *count-1 && *delaySec > 0 {
			time.Sleep(time.Duration(*delaySec) * time.Second)
		}
	}
	log.Printf("Done. Successfully generated %d/%d accounts.", generated, *count)
}

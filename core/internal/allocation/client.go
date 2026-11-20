// Package allocation retrieves only the account assigned to this installation.
package allocation

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
)

const Endpoint = "https://85-accounts.parsaghelich062.workers.dev/v1/account"

type Account struct {
	Email        string `json:"email"`
	RefreshToken string `json:"refresh_token"`
}

func Credential(dir string) (string, error) {
	if err := os.MkdirAll(dir, 0700); err != nil {
		return "", err
	}
	path := filepath.Join(dir, "allocation.key")
	data, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		secret := make([]byte, 32)
		if _, err = rand.Read(secret); err != nil {
			return "", err
		}
		f, createErr := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
		if createErr != nil {
			return "", createErr
		}
		data = []byte(hex.EncodeToString(secret))
		_, err = f.Write(data)
		closeErr := f.Close()
		if err != nil {
			return "", err
		}
		if closeErr != nil {
			return "", closeErr
		}
	} else if err != nil {
		return "", err
	}
	if len(data) != 64 {
		return "", errors.New("invalid installation credential")
	}
	if _, err := hex.DecodeString(string(data)); err != nil {
		return "", errors.New("invalid installation credential")
	}
	if err := os.Chmod(path, 0600); err != nil {
		return "", err
	}
	return string(data), nil
}

func Fetch(ctx context.Context, endpoint, credential string, client *http.Client) (Account, error) {
	var account Account
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, endpoint, bytes.NewBufferString("{}"))
	if err != nil {
		return account, errors.New("invalid allocation endpoint")
	}
	req.Header.Set("Authorization", "Bearer "+credential)
	req.Header.Set("Content-Type", "application/json")
	// Never forward installation credentials through redirects.
	copyClient := *client
	copyClient.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	resp, err := copyClient.Do(req)
	if err != nil {
		return account, errors.New("account allocation connection failed")
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return account, fmt.Errorf("account allocation unavailable (HTTP %d)", resp.StatusCode)
	}
	data, err := io.ReadAll(io.LimitReader(resp.Body, 32769))
	if err != nil || len(data) > 32768 {
		return account, errors.New("invalid allocation response")
	}
	if json.Unmarshal(data, &account) != nil || account.Email == "" || account.RefreshToken == "" {
		return Account{}, errors.New("invalid allocation response")
	}
	return account, nil
}

package proxy

import (
	"context"
	"encoding/json"
	"errors"
	"net/http"
	"testing"

	"github.com/III85III/ONYX/core/internal/fxa"
)

func TestDeviceBrandRenameKeepsRegistrationAndCredentials(t *testing.T) {
	pool, list := newTestAccountPool(t, "a")
	id := list["a"].ID
	creds, err := pool.store.Credentials(id)
	if err != nil {
		t.Fatal(err)
	}
	fingerprint := tokenFingerprint(creds.RefreshToken)
	if err := pool.store.SaveDevice(id, "existing-device", fingerprint, "Previous device name"); err != nil {
		t.Fatal(err)
	}
	requests := 0
	setCoreControlPlane(t, func(r *http.Request) (*http.Response, error) {
		if r.URL.Host != "api.accounts.firefox.com" || r.URL.Path != "/v1/account/device" {
			return nil, errors.New("unexpected request")
		}
		var body map[string]string
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			t.Fatal(err)
		}
		if body["id"] != "existing-device" || body["name"] != "III85III VPN" {
			t.Fatalf("rename must update the existing device with the product name")
		}
		requests++
		return coreResponse(r, http.StatusOK, `{"id":"existing-device","name":"III85III VPN"}`, nil), nil
	})
	DeviceRegistration.Store(true)
	t.Cleanup(func() { DeviceRegistration.Store(false) })
	pool.EnsureDevice(context.Background(), id)
	pool.EnsureDevice(context.Background(), id)
	got, err := pool.store.Get(id)
	if err != nil {
		t.Fatal(err)
	}
	if requests != 1 || got.DeviceID != "existing-device" || got.DeviceName != fxa.DeviceName {
		t.Fatalf("device rename was not saved exactly once")
	}
	after, err := pool.store.Credentials(id)
	if err != nil || after.RefreshToken != creds.RefreshToken || after.AccessToken != creds.AccessToken {
		t.Fatal("device rename changed account credentials")
	}
}

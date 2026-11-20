package config

import (
	"path/filepath"
	"testing"
)

func TestNewProfileDoesNotHideExistingAccounts(t *testing.T) {
	current, legacy := filepath.Join("profiles", "ONYX VPN"), filepath.Join("profiles", "legacy")
	files := map[string]bool{}
	exists := func(path string) bool { return files[path] }
	if got := existingProfileDir(current, legacy, exists); got != current {
		t.Fatal("fresh install must use product profile")
	}
	files[filepath.Join(legacy, "accounts")] = true
	if got := existingProfileDir(current, legacy, exists); got != legacy {
		t.Fatal("existing accounts must not disappear")
	}
	files[filepath.Join(current, FileName)] = true
	if got := existingProfileDir(current, legacy, exists); got != current {
		t.Fatal("existing product profile must take priority")
	}
}

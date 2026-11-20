//go:build !windows

package update

import (
	"errors"
	"time"
)

// Updates install by themselves on Windows only; elsewhere the app offers
// the download.
const platformSupported = false

var errNotSupported = errors.New("updates install by themselves on Windows only")

func updating(string) bool { return false }

func lockApp(string) (func(), error) { return nil, errNotSupported }

func waitForExit([]int, time.Duration) error { return errNotSupported }

func startUpdater(string, string) error { return errNotSupported }

func startApp(string, []string) error { return errNotSupported }

func setInstalledVersion(string, string) error { return nil }

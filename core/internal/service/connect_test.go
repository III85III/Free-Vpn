package service

import (
	"context"
	"net/http"
	"testing"
	"time"

	"github.com/III85III/ONYX/core/internal/netcheck"
	"github.com/III85III/ONYX/core/internal/proxy"
)

func shortConnectWindow(t *testing.T) {
	t.Helper()
	old := connectWindow
	connectWindow = 100 * time.Millisecond
	t.Cleanup(func() { connectWindow = old })
}

func connectFailure(t *testing.T, d *Daemon) string {
	t.Helper()
	view, err := d.StatusView()
	if err != nil {
		t.Fatal(err)
	}
	return view.ConnectFailure
}

// A connect that never reaches Mozilla is given up: the proxy goes off,
// and the status says why.
func TestConnectGivesUpWhenNoAttemptCompletes(t *testing.T) {
	shortConnectWindow(t)
	d := newTestDaemon(t) // no supervisor, so no wake can complete
	d.Connect()
	waitForCondition(t, time.Second, func() bool { return !d.IsOn() })
	if got := connectFailure(t, d); got != proxy.FailureOther {
		t.Fatalf("reason %q, want %q", got, proxy.FailureOther)
	}
	// The next connect starts without the old reason.
	d.Connect()
	if got := connectFailure(t, d); got != "" {
		t.Fatalf("the old reason stays: %q", got)
	}
}

// One connection to Mozilla is enough; the proxy stays on after.
func TestConnectThatReachedMozillaStaysOn(t *testing.T) {
	d, f := runLazyDaemon(t)
	d.SetOn(false)
	waitMessage(t, d, PhaseOff, "Proxy off")
	d.Connect()
	waitForCondition(t, 5*time.Second, func() bool {
		view, _ := d.StatusView()
		return view.Pass != nil && !view.ConnectPending
	})
	if !d.IsOn() || connectFailure(t, d) != "" {
		t.Fatalf("on %t, reason %q", d.IsOn(), connectFailure(t, d))
	}
	if got := f.passes.Load(); got != 1 {
		t.Fatalf("the button must wake the proxy without client traffic: got %d pass requests", got)
	}
}

func TestConnectRetriesBeforePublishingFailure(t *testing.T) {
	oldCheck := netcheck.Check
	netcheck.Check = func(context.Context) netcheck.Result { return netcheck.Online }
	t.Cleanup(func() { netcheck.Check = oldCheck })

	d, f := runLazyDaemon(t)
	d.SetOn(false)
	waitMessage(t, d, PhaseOff, "Proxy off")
	f.guardianFails.Store(true)
	d.Connect()
	waitForCondition(t, 5*time.Second, func() bool {
		view, _ := d.StatusView()
		return view.Proxy != nil && view.Proxy.WakeError != ""
	})
	if view, _ := d.StatusView(); !view.ConnectPending || view.ConnectFailure != "" || !d.IsOn() {
		t.Fatalf("the first failure must leave the connect pending: pending=%t failure=%q", view.ConnectPending, view.ConnectFailure)
	}
	waitMessage(t, d, PhaseOff, "Proxy off")
	view, _ := d.StatusView()
	if got := f.passes.Load(); got != 2 || view.ConnectPending || view.ConnectFailure != proxy.FailureOther {
		t.Fatalf("after retries: %d pass requests, pending=%t, failure=%q", got, view.ConnectPending, view.ConnectFailure)
	}
}

// The app's button gives up; a browser's (a server's dashboard) does not.
func TestPowerFromABrowserIsNotGivenUp(t *testing.T) {
	shortConnectWindow(t)
	d := runOffDaemon(t, "clear")
	if rec := newDashboardClient(t, d).do(http.MethodPost, "/api/power", map[string]bool{"on": true}); rec.Code != http.StatusOK {
		t.Fatalf("on: %d", rec.Code)
	}
	waitMessage(t, d, PhaseRunning, "Proxy running")
	time.Sleep(300 * time.Millisecond)
	if !d.IsOn() {
		t.Fatal("a browser's connect was given up")
	}
}

// A daemon set off before it runs (the desktop and phone apps start it so)
// says so from the first status: the app shows a starting state as
// connecting, and routing loads before the run reaches the engine.
func TestADaemonSetOffSaysOffBeforeItRuns(t *testing.T) {
	d := newTestDaemon(t)
	d.Off()
	if d.state.Phase != PhaseOff || d.state.Message != "Proxy off" {
		t.Fatalf("state %q %q before the run", d.state.Phase, d.state.Message)
	}
}

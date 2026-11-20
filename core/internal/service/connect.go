package service

import (
	"context"
	"time"

	"github.com/III85III/ONYX/core/internal/logx"
	"github.com/III85III/ONYX/core/internal/netstate"
	"github.com/III85III/ONYX/core/internal/proxy"
)

// An explicit connect owns its first wake and one retry within connectWindow.
// A wake can already try more than one endpoint or transport.
var connectWindow = time.Minute

const (
	connectWakeLimit  = 2
	connectPoll       = 200 * time.Millisecond
	connectRetryDelay = 2 * time.Second
)

type connectAttempt struct {
	ctx      context.Context
	cancel   context.CancelFunc
	failures int // guarded by Daemon.mu
}

// Connect turns the proxy on for the user of an app, and gives the connect
// up after connectWindow unless it reaches Mozilla.
func (d *Daemon) Connect() {
	ctx, cancel := context.WithTimeout(context.Background(), connectWindow)
	attempt := &connectAttempt{ctx: ctx, cancel: cancel}
	d.mu.Lock()
	d.endConnectLocked()
	d.autoConnect = nil
	d.connectAttempt, d.connectFailure = attempt, ""
	d.lastTest = nil
	wasOff := d.off
	d.off = false
	d.mu.Unlock()
	if wasOff {
		logx.Info("proxy turned on=true")
		d.RequestRestart()
	}
	go d.retryConnect(attempt)
	go d.watchConnect(attempt)
}

// markConnected completes an explicit connect only for the current engine.
func (d *Daemon) markConnected(engine *proxyEngine, attempt *connectAttempt) {
	d.mu.Lock()
	if d.connectAttempt == attempt && d.engine == engine && !d.off {
		d.endConnectLocked()
	}
	d.mu.Unlock()
}

// ConnectPending reports that the explicit connection can still retry.
func (d *Daemon) ConnectPending() bool {
	d.mu.Lock()
	defer d.mu.Unlock()
	return d.connectAttempt != nil
}

// endConnect ends a connect attempt: the user acted.
func (d *Daemon) endConnect() {
	d.mu.Lock()
	d.endConnectLocked()
	d.mu.Unlock()
}

func (d *Daemon) endConnectLocked() {
	if d.connectAttempt != nil {
		d.connectAttempt.cancel()
		d.connectAttempt = nil
	}
}

// retryConnect drives the same wake path as client traffic. It respects
// wakeRetryAt and shares the wake lock with any incoming client or test.
func (d *Daemon) retryConnect(attempt *connectAttempt) {
	ticker := time.NewTicker(connectPoll)
	defer ticker.Stop()
	for attempt.ctx.Err() == nil {
		if engine := d.currentEngine(); engine != nil {
			if _, release, err := d.useController(engine, wakeForClient); err == nil {
				release()
				d.markConnected(engine, attempt)
				return
			}
		}
		select {
		case <-d.stop:
			return
		case <-attempt.ctx.Done():
			return
		case <-ticker.C:
		}
	}
}

func (d *Daemon) watchConnect(attempt *connectAttempt) {
	select {
	case <-d.stop:
		attempt.cancel()
		return
	case <-attempt.ctx.Done():
	}
	if attempt.ctx.Err() != context.DeadlineExceeded {
		return
	}
	// A still-running call has no confirmed cause. Use a neutral final
	// message unless the supervisor knows what prevented it from starting.
	d.mu.Lock()
	reason := proxy.FailureOther
	switch d.state.Code {
	case CodeWaitingForNetwork:
		reason = proxy.FailureNoNetwork
	case CodeNoAccounts, CodeNoUsableAccount:
		reason = proxy.FailureNoAccount
	}
	d.mu.Unlock()
	if netstate.ReportedDown() {
		reason = proxy.FailureNoNetwork
	}
	d.failConnect(attempt, reason)
}

// noteConnectFailure counts actual wakes, not requests rejected during
// backoff. A quota or account error that cannot improve soon ends the wait.
func (d *Daemon) noteConnectFailure(engine *proxyEngine, attempt *connectAttempt, code string, lasting bool) {
	if attempt == nil {
		return
	}
	d.mu.Lock()
	if d.connectAttempt != attempt || d.engine != engine || d.off || attempt.ctx.Err() != nil {
		d.mu.Unlock()
		return
	}
	attempt.failures++
	done := lasting || attempt.failures >= connectWakeLimit
	d.mu.Unlock()
	if done {
		d.failConnect(attempt, code)
	}
}

func (d *Daemon) failConnect(attempt *connectAttempt, reason string) {
	d.mu.Lock()
	if d.connectAttempt != attempt || d.off {
		d.mu.Unlock()
		return
	}
	d.endConnectLocked()
	d.connectFailure, d.off = reason, true
	d.mu.Unlock()
	logx.Warn("connection attempts ended; the proxy is off again reason=%s", reason)
	d.RequestRestart()
}

// RetryNow drops the waits that failures left before the next attempt, for
// a user who is looking (the app came to the front): the next connection
// tries at once. Failures that retrying cannot fix (a spent quota, an
// account that must sign in again) keep theirs.
func (d *Daemon) RetryNow() {
	engine := d.currentEngine()
	if engine == nil {
		return
	}
	engine.retryNow()
	if controller := engine.currentController(); controller != nil {
		controller.RetryNow()
	}
}

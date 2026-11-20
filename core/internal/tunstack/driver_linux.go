package tunstack

import (
	"context"
	"errors"
	"io/fs"
	"os/exec"
	"strings"
	"time"

	"github.com/III85III/ONYX/core/internal/logx"
)

// missingDriver reports whether opening the adapter failed for want of the
// kernel's TUN driver: /dev/net/tun has no driver behind it, or is not there.
func missingDriver(err error) bool {
	if err == nil {
		return false
	}
	text := err.Error()
	return strings.Contains(text, "/dev/net/tun") && strings.Contains(text, "no such")
}

// loadDriver asks the kernel to load the TUN driver, for a kernel that has it
// as a module not yet loaded. After a kernel update it cannot: the running
// kernel's modules are gone until the computer restarts.
func loadDriver() {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	for _, path := range []string{"/usr/sbin/modprobe", "/sbin/modprobe"} {
		out, err := exec.CommandContext(ctx, path, "tun").CombinedOutput()
		if errors.Is(err, fs.ErrNotExist) {
			continue
		}
		if err != nil {
			logx.Warn("loading the TUN driver failed err=%s output=%s", logx.Err(err), strings.TrimSpace(string(out)))
		} else {
			logx.Info("loaded the TUN driver")
		}
		return
	}
	logx.Warn("loading the TUN driver failed: no modprobe")
}

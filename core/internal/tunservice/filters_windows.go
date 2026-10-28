package tunservice

import (
	"io"

	"github.com/III85III/III85III-VPN/core/internal/wfp"
)

// startFilters keeps DNS (and, for an adapter without IPv6, IPv6) inside
// the adapter, for every program but these.
func startFilters(index uint32, programs []string, blockIPv6 bool) (io.Closer, error) {
	return wfp.Start(wfp.Rules{Interface: index, Programs: programs, BlockIPv6: blockIPv6})
}

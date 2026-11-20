package tunstack

import (
	"errors"
	"reflect"
	"unsafe"

	"github.com/sagernet/gvisor/pkg/tcpip"
	"github.com/sagernet/gvisor/pkg/tcpip/stack"
	"github.com/sagernet/gvisor/pkg/tcpip/transport/tcp"
	tun "github.com/sagernet/sing-tun"
	"golang.org/x/sys/windows"
)

// The adapter's description names this program, not sing-tun.
func init() { tun.TunnelType = "Firefox VPN" }

func deviceName(name string) string { return name }

// platformOptions: the service's own filters (internal/wfp) keep DNS in
// and IPv6 out, not sing-tun's strict route.
func platformOptions(*tun.Options, Config) {}

// CleanupStale removes what a TUN that ended without closing left; on
// Windows the adapter and the filters go with the process.
func CleanupStale() error { return nil }

// Interface is the adapter's name, interface index, and LUID.
func (d *Device) Interface() (name string, index uint32, luid uint64, err error) {
	l, ok := d.tun.(interface{ LUID() uint64 })
	if !ok {
		return "", 0, 0, errors.New("tunstack: the adapter has no LUID")
	}
	row := windows.MibIfRow2{InterfaceLuid: l.LUID()}
	if err := windows.GetIfEntry2Ex(windows.MibIfEntryNormal, &row); err != nil {
		return "", 0, 0, err
	}
	return d.cfg.Name, row.InterfaceIndex, row.InterfaceLuid, nil
}

// tuneGVisorStack scales gVisor TCP receive and send windows from mobile 32KB/128KB
// defaults to desktop high-speed throughput (8MB default / 32MB max) with buffer moderation.
func tuneGVisorStack(s tun.Stack) {
	if s == nil {
		return
	}
	val := reflect.ValueOf(s)
	if val.Kind() == reflect.Pointer {
		val = val.Elem()
	}
	if val.Kind() != reflect.Struct {
		return
	}
	field := val.FieldByName("stack")
	if !field.IsValid() || !field.CanAddr() {
		return
	}
	ipStackPtr := *(**stack.Stack)(unsafe.Pointer(field.UnsafeAddr()))
	if ipStackPtr == nil {
		return
	}

	rxBuf := tcpip.TCPReceiveBufferSizeRangeOption{
		Min:     8192,
		Default: 8 * 1024 * 1024,
		Max:     32 * 1024 * 1024,
	}
	_ = ipStackPtr.SetTransportProtocolOption(tcp.ProtocolNumber, &rxBuf)

	txBuf := tcpip.TCPSendBufferSizeRangeOption{
		Min:     8192,
		Default: 8 * 1024 * 1024,
		Max:     32 * 1024 * 1024,
	}
	_ = ipStackPtr.SetTransportProtocolOption(tcp.ProtocolNumber, &txBuf)

	modBuf := tcpip.TCPModerateReceiveBufferOption(true)
	_ = ipStackPtr.SetTransportProtocolOption(tcp.ProtocolNumber, &modBuf)

	sack := tcpip.TCPSACKEnabled(true)
	_ = ipStackPtr.SetTransportProtocolOption(tcp.ProtocolNumber, &sack)
}

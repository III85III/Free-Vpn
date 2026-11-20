//go:build !linux

package tun

import "errors"

// Tunnel is a running userspace stack on a TUN device; only Android (Linux)
// has one.
type Tunnel struct{}

// Start is not supported here: the VPN mode is Android's.
// AllowUDP reports whether the VPN carries UDP to host:port.
type AllowUDP func(host, port string) bool

func Start(fd int, mtu uint32, dial Dialer, associate UDPDialer, allow Allow, allowUDP AllowUDP, dns *FakeDNS) (*Tunnel, error) {
	return nil, errors.New("tun: the VPN mode runs on Android only")
}

func (t *Tunnel) Stop() {}

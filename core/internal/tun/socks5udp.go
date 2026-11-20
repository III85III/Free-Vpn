package tun

import (
	"context"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"net"
	"net/netip"
	"strconv"
	"sync"
	"time"
)

// UDP through the proxy: SOCKS5's UDP ASSOCIATE (RFC 1928 7). The association
// is one app socket's way out: packets name their destination, and the proxy
// keeps a tunnel for each destination, so that one socket can talk to several
// peers and two sockets to one peer stay apart.

// UDPDialer opens an association with the proxy.
type UDPDialer func(ctx context.Context) (*UDPAssociation, error)

// UDPAssociation is one SOCKS5 UDP association: its TCP connection, which
// keeps it alive, and a UDP socket connected to the proxy's relay.
type UDPAssociation struct {
	ctrl net.Conn
	conn *net.UDPConn
	once sync.Once
}

// SOCKS5Associate asks the SOCKS5 proxy at addr, with its login if user is not
// empty, for a UDP association.
func SOCKS5Associate(ctx context.Context, addr, user, password string) (*UDPAssociation, error) {
	var d net.Dialer
	ctrl, err := d.DialContext(ctx, "tcp", addr)
	if err != nil {
		return nil, err
	}
	stop := context.AfterFunc(ctx, func() { _ = ctrl.Close() })
	defer stop()
	relay, err := socks5AssociateRequest(ctrl, user, password)
	if err != nil {
		_ = ctrl.Close()
		return nil, err
	}
	if relay.Addr().IsUnspecified() {
		// Reached through the address the TCP connection went to.
		remote, ok := ctrl.RemoteAddr().(*net.TCPAddr)
		if !ok {
			_ = ctrl.Close()
			return nil, errors.New("the SOCKS5 proxy's relay has no address")
		}
		ip, _ := netip.AddrFromSlice(remote.IP)
		relay = netip.AddrPortFrom(ip.Unmap(), relay.Port())
	}
	conn, err := net.DialUDP("udp", nil, net.UDPAddrFromAddrPort(relay))
	if err != nil {
		_ = ctrl.Close()
		return nil, err
	}
	a := &UDPAssociation{ctrl: ctrl, conn: conn}
	go func() {
		// The association ends when the proxy closes this connection.
		_, _ = io.Copy(io.Discard, ctrl)
		a.Close()
	}()
	return a, nil
}

// socks5AssociateRequest runs the handshake on ctrl and returns the relay's
// address.
func socks5AssociateRequest(ctrl net.Conn, user, password string) (netip.AddrPort, error) {
	method := byte(0x00)
	if user != "" {
		method = 0x02
	}
	if _, err := ctrl.Write([]byte{0x05, 0x01, method}); err != nil {
		return netip.AddrPort{}, err
	}
	var reply [2]byte
	if _, err := io.ReadFull(ctrl, reply[:]); err != nil {
		return netip.AddrPort{}, err
	}
	if reply[0] != 0x05 || reply[1] != method {
		return netip.AddrPort{}, fmt.Errorf("the SOCKS5 proxy chose method %d", reply[1])
	}
	if method == 0x02 {
		login := []byte{0x01, byte(len(user))}
		login = append(login, user...)
		login = append(login, byte(len(password)))
		login = append(login, password...)
		if _, err := ctrl.Write(login); err != nil {
			return netip.AddrPort{}, err
		}
		if _, err := io.ReadFull(ctrl, reply[:]); err != nil {
			return netip.AddrPort{}, err
		}
		if reply[1] != 0x00 {
			return netip.AddrPort{}, errors.New("the SOCKS5 proxy refused the login")
		}
	}
	// UDP ASSOCIATE, from an address not known yet.
	if _, err := ctrl.Write([]byte{0x05, 0x03, 0x00, 0x01, 0, 0, 0, 0, 0, 0}); err != nil {
		return netip.AddrPort{}, err
	}
	var head [4]byte
	if _, err := io.ReadFull(ctrl, head[:]); err != nil {
		return netip.AddrPort{}, err
	}
	if head[1] != 0x00 {
		return netip.AddrPort{}, fmt.Errorf("the SOCKS5 proxy refused UDP (reply %d)", head[1])
	}
	size := 4
	switch head[3] {
	case 0x01:
	case 0x04:
		size = 16
	default:
		return netip.AddrPort{}, fmt.Errorf("the SOCKS5 proxy's relay has address type %d", head[3])
	}
	raw := make([]byte, size+2)
	if _, err := io.ReadFull(ctrl, raw); err != nil {
		return netip.AddrPort{}, err
	}
	ip, _ := netip.AddrFromSlice(raw[:size])
	return netip.AddrPortFrom(ip.Unmap(), binary.BigEndian.Uint16(raw[size:])), nil
}

// Send sends p to target (host:port).
func (a *UDPAssociation) Send(target string, p []byte) error {
	host, portText, err := net.SplitHostPort(target)
	if err != nil {
		return err
	}
	port, err := strconv.ParseUint(portText, 10, 16)
	if err != nil {
		return err
	}
	packet := make([]byte, 0, len(p)+len(host)+7)
	packet = append(packet, 0, 0, 0)
	if ip, err := netip.ParseAddr(host); err == nil {
		if ip.Is4() {
			packet = append(packet, 0x01)
		} else {
			packet = append(packet, 0x04)
		}
		packet = append(packet, ip.AsSlice()...)
	} else {
		if len(host) > 255 {
			return errors.New("the name is too long for SOCKS5")
		}
		packet = append(packet, 0x03, byte(len(host)))
		packet = append(packet, host...)
	}
	packet = binary.BigEndian.AppendUint16(packet, uint16(port))
	packet = append(packet, p...)
	_, err = a.conn.Write(packet)
	return err
}

// Recv reads the next reply into b (a longer one is cut), and who it comes
// from, as Send's target names it.
func (a *UDPAssociation) Recv(b []byte) (int, string, error) {
	buf := make([]byte, 65535+262)
	for {
		n, err := a.conn.Read(buf)
		if err != nil {
			if errors.Is(err, net.ErrClosed) {
				return 0, "", err
			}
			// An ICMP unreachable from the relay's socket, on some systems;
			// a closed relay also ends the TCP connection.
			time.Sleep(10 * time.Millisecond)
			continue
		}
		from, payload, ok := parseSOCKS5UDP(buf[:n])
		if !ok {
			continue
		}
		return copy(b, payload), from, nil
	}
}

// parseSOCKS5UDP reads a relay's datagram: who it comes from, and the packet.
func parseSOCKS5UDP(b []byte) (string, []byte, bool) {
	if len(b) < 4 || b[2] != 0 {
		return "", nil, false
	}
	at := 4
	var host string
	switch b[3] {
	case 0x01, 0x04:
		size := 4
		if b[3] == 0x04 {
			size = 16
		}
		if len(b) < at+size {
			return "", nil, false
		}
		ip, _ := netip.AddrFromSlice(b[at : at+size])
		host = ip.String()
		at += size
	case 0x03:
		if len(b) < at+1 || len(b) < at+1+int(b[at]) {
			return "", nil, false
		}
		host = string(b[at+1 : at+1+int(b[at])])
		at += 1 + int(b[at])
	default:
		return "", nil, false
	}
	if len(b) < at+2 {
		return "", nil, false
	}
	port := binary.BigEndian.Uint16(b[at:])
	return net.JoinHostPort(host, strconv.Itoa(int(port))), b[at+2:], true
}

// Close ends the association.
func (a *UDPAssociation) Close() {
	a.once.Do(func() {
		_ = a.ctrl.Close()
		_ = a.conn.Close()
	})
}

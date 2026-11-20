package tunservice

import (
	"context"
	"net"
	"net/netip"
	"sync"
	"time"

	"github.com/III85III/ONYX/core/internal/egress"
	"github.com/III85III/ONYX/core/internal/logx"
	"github.com/III85III/ONYX/core/internal/tun"
	"github.com/III85III/ONYX/core/internal/tunapi"
	"github.com/III85III/ONYX/core/internal/tunstack"
)

// UDP in the TUN: a flow is one app port's packets. Its program may skip the
// VPN (programs.go): each packet then goes to its destination outside the
// TUN, a fake address by its name, which the physical network's DNS resolves.
// The other packets go to the core, on a SOCKS5 UDP association, when Mozilla
// carries their port (the core's list, open's UDPPorts); they keep their name
// there, which Mozilla's proxy resolves. Either way the replies come back to
// the app from the address it sent to. Replaced in tests.
var (
	listenUDPDirect = egress.ListenUDP
	lookupDirect    = egress.LookupNetIP
)

const (
	// udpLookupTimeout bounds resolving a fake address's name.
	udpLookupTimeout = 5 * time.Second
	// udpAssociateTimeout bounds asking the core for an association.
	udpAssociateTimeout = 5 * time.Second
	// udpAssociateRetry is how long packets are dropped after that failed.
	udpAssociateRetry = 2 * time.Second
)

// packetFlow is the part of tunstack.PacketFlow the relay uses; tests fake
// it.
type packetFlow interface {
	Accept()
	Read(b []byte) (int, netip.AddrPort, error)
	Write(b []byte, from netip.AddrPort) error
}

// udpRelay carries one flow: one socket per address family for the packets
// that go directly, opened when the app first sends to one, and an association
// with the core for the others, likewise.
type udpRelay struct {
	sess   *session
	flow   packetFlow
	source netip.AddrPort
	// names are the real addresses of the fake ones the app sent to.
	names map[netip.Addr]netip.Addr
	mu    sync.Mutex
	// back maps a real address the app's packets went to back to the one
	// it sent them to; vpnBack does for a target at the core.
	back         map[netip.AddrPort]netip.AddrPort
	v4, v6       *net.UDPConn
	vpnBack      map[string]netip.AddrPort
	assoc        *tun.UDPAssociation
	assocRetryAt time.Time
	closed       bool
}

// relayUDP carries a flow takeUDP took until it ends.
func (s *session) relayUDP(f *tunstack.PacketFlow) { s.relay(f.Source, f) }

func (s *session) relay(source netip.AddrPort, f packetFlow) {
	r := &udpRelay{
		sess:    s,
		flow:    f,
		source:  source,
		names:   map[netip.Addr]netip.Addr{},
		back:    map[netip.AddrPort]netip.AddrPort{},
		vpnBack: map[string]netip.AddrPort{},
	}
	defer r.close()
	f.Accept()
	b := make([]byte, 65535)
	for {
		n, dst, err := f.Read(b)
		if err != nil {
			return
		}
		switch s.udpRoute(source, dst) {
		case udpDirect:
			r.sendDirect(dst, b[:n])
		case udpThroughVPN:
			r.sendVPN(dst, b[:n])
		}
	}
}

// sendDirect sends a packet outside the TUN.
func (r *udpRelay) sendDirect(dst netip.AddrPort, p []byte) {
	peer, ok := r.resolve(dst)
	if !ok {
		return
	}
	conn, err := r.socket(peer.Addr())
	if err != nil {
		logx.Debug("TUN: no direct UDP socket for dst=%s err=%s", peer, logx.Err(err))
		return
	}
	r.mu.Lock()
	r.back[peer] = dst
	r.mu.Unlock()
	_, _ = conn.WriteToUDPAddrPort(p, peer)
}

// sendVPN gives a packet to the core: to the name a fake address stands for,
// which Mozilla's proxy resolves.
func (r *udpRelay) sendVPN(dst netip.AddrPort, p []byte) {
	target, ok := r.sess.dns.Target(dst)
	if !ok {
		return
	}
	a := r.association()
	if a == nil {
		return
	}
	r.mu.Lock()
	r.vpnBack[target] = dst
	r.mu.Unlock()
	if err := a.Send(target, p); err != nil {
		logx.Debug("TUN: sending UDP to the core failed err=%s", logx.Err(err))
	}
}

// association is the flow's association with the core, opened when first
// needed and again after the core closed it.
func (r *udpRelay) association() *tun.UDPAssociation {
	r.mu.Lock()
	if r.assoc != nil || r.closed || time.Now().Before(r.assocRetryAt) {
		a := r.assoc
		r.mu.Unlock()
		return a
	}
	r.mu.Unlock()
	ctx, cancel := context.WithTimeout(r.sess.ctx, udpAssociateTimeout)
	a, err := r.sess.associate(ctx)
	cancel()
	r.mu.Lock()
	defer r.mu.Unlock()
	if err != nil {
		r.assocRetryAt = time.Now().Add(udpAssociateRetry)
		if r.sess.ctx.Err() == nil {
			logx.Debug("TUN: no UDP association with the core err=%s", logx.Err(err))
		}
		return nil
	}
	if r.closed {
		a.Close()
		return nil
	}
	r.assoc = a
	go r.readVPN(a)
	return a
}

// readVPN writes each reply from the core to the app, from the address it
// sent to; the association may end (the core restarted), and the next packet
// opens a new one.
func (r *udpRelay) readVPN(a *tun.UDPAssociation) {
	b := make([]byte, 65535)
	for {
		n, from, err := a.Recv(b)
		if err != nil {
			a.Close()
			r.mu.Lock()
			if r.assoc == a {
				r.assoc = nil
			}
			r.mu.Unlock()
			return
		}
		r.mu.Lock()
		to, ok := r.vpnBack[from]
		r.mu.Unlock()
		if ok {
			_ = r.flow.Write(b[:n], to)
		}
	}
}

// resolve is where a packet to dst really goes: dst, or the address of the
// name a fake one stands for; not a filtering DNS's answer.
func (r *udpRelay) resolve(dst netip.AddrPort) (netip.AddrPort, bool) {
	addr := dst.Addr().Unmap()
	if isFilterAddress(addr) {
		return netip.AddrPort{}, false
	}
	if !tun.IsFake(addr) {
		return netip.AddrPortFrom(addr, dst.Port()), true
	}
	if peer, ok := r.names[addr]; ok {
		return netip.AddrPortFrom(peer, dst.Port()), true
	}
	name, ok := r.sess.dns.Name(addr)
	if !ok {
		return netip.AddrPort{}, false
	}
	ctx, cancel := context.WithTimeout(r.sess.ctx, udpLookupTimeout)
	addrs, err := lookupDirect(ctx, name)
	cancel()
	if err != nil || len(addrs) == 0 {
		logx.Debug("TUN: resolving name=%s for direct UDP failed err=%s", name, logx.Err(err))
		return netip.AddrPort{}, false
	}
	// IPv4 first: the app sent from IPv4, to a fake IPv4 address.
	peer := addrs[0].Unmap()
	for _, a := range addrs {
		if a.Unmap().Is4() {
			peer = a.Unmap()
			break
		}
	}
	r.names[addr] = peer
	return netip.AddrPortFrom(peer, dst.Port()), true
}

// socket is the flow's socket for addr's family.
func (r *udpRelay) socket(addr netip.Addr) (*net.UDPConn, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	slot := &r.v4
	if addr.Is6() {
		slot = &r.v6
	}
	if *slot != nil {
		return *slot, nil
	}
	conn, err := listenUDPDirect(addr)
	if err != nil {
		return nil, err
	}
	*slot = conn
	go r.readBack(conn)
	return conn, nil
}

// readBack writes each reply to the app, from the address it sent to.
func (r *udpRelay) readBack(conn *net.UDPConn) {
	b := make([]byte, 65535)
	for {
		n, from, err := conn.ReadFromUDPAddrPort(b)
		if err != nil {
			return
		}
		from = netip.AddrPortFrom(from.Addr().Unmap(), from.Port())
		r.mu.Lock()
		to, ok := r.back[from]
		r.mu.Unlock()
		if !ok {
			// A peer the app never sent to (a call's other end): as it is.
			to = from
		}
		_ = r.flow.Write(b[:n], to)
	}
}

func (r *udpRelay) close() {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.closed = true
	for _, c := range []*net.UDPConn{r.v4, r.v6} {
		if c != nil {
			_ = c.Close()
		}
	}
	if r.assoc != nil {
		r.assoc.Close()
	}
}

// udpVerdict is what happens to a UDP packet.
type udpVerdict int

const (
	udpRefused udpVerdict = iota
	udpDirect
	udpThroughVPN
)

// carriesUDP reports whether UDP to dst goes through the VPN: to an address on
// the internet (a fake one stands for a name), on a port the core says Mozilla
// carries.
func (s *session) carriesUDP(dst netip.AddrPort) bool {
	addr := dst.Addr().Unmap()
	if isFilterAddress(addr) || !(tun.IsFake(addr) || (addr.IsGlobalUnicast() && !addr.IsPrivate())) {
		return false
	}
	for _, r := range s.udpPorts {
		if dst.Port() >= r.From && dst.Port() <= r.To {
			return true
		}
	}
	return false
}

// udpRoute decides on a packet from an app's port to dst. A program whose
// traffic skips the VPN sends it directly, and one whose UDP alone does, but
// not QUIC (port 443), so that it falls back to TCP through the VPN. Anything
// else goes through the VPN if it can; otherwise it is refused, and a call or
// a game told to the core (the app offers to send the program directly).
func (s *session) udpRoute(source, dst netip.AddrPort) udpVerdict {
	set := s.programSet()
	carried := s.carriesUDP(dst)
	call := callLike(dst)
	if set.size() == 0 {
		if carried {
			return udpThroughVPN
		}
		if !call {
			return udpRefused
		}
	}
	path := s.owners.udp(source)
	name := programName(path)
	if udpOnly, ok := set.lookup(name); ok && (!udpOnly || dst.Port() != 443) {
		return udpDirect
	}
	if carried {
		return udpThroughVPN
	}
	if _, ok := set.lookup(name); !ok && call && name != "" && !tunapi.SystemProgram(path) {
		s.tellRefused(name, path)
	}
	return udpRefused
}

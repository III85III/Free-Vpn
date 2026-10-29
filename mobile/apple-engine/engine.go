// Package appleengine adapts the unchanged account engine to Apple's public
// packetFlow API. It does not obtain a private utun file descriptor.
package appleengine

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/netip"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/III85III/III85III-VPN/core/internal/config"
	"github.com/III85III/III85III-VPN/core/internal/tun"
	"github.com/III85III/III85III-VPN/core/mobile"
	"github.com/sagernet/gvisor/pkg/buffer"
	"github.com/sagernet/gvisor/pkg/tcpip"
	"github.com/sagernet/gvisor/pkg/tcpip/adapters/gonet"
	"github.com/sagernet/gvisor/pkg/tcpip/header"
	"github.com/sagernet/gvisor/pkg/tcpip/link/channel"
	"github.com/sagernet/gvisor/pkg/tcpip/network/ipv4"
	"github.com/sagernet/gvisor/pkg/tcpip/network/ipv6"
	"github.com/sagernet/gvisor/pkg/tcpip/stack"
	"github.com/sagernet/gvisor/pkg/tcpip/transport/icmp"
	"github.com/sagernet/gvisor/pkg/tcpip/transport/tcp"
	"github.com/sagernet/gvisor/pkg/tcpip/transport/udp"
	"github.com/sagernet/gvisor/pkg/waiter"
)

var engineMu sync.Mutex
var directory, endpoint string
var packetsMu sync.RWMutex
var packets *packetBridge

// StartEngine must run in either the foreground application or the tunnel
// extension, never both at once for the same application-group directory.
func StartEngine(dataDir string, on bool) error {
	engineMu.Lock()
	defer engineMu.Unlock()
	if mobile.Running() {
		return nil
	}
	if err := os.MkdirAll(dataDir, 0700); err != nil {
		return err
	}
	cfg, _, err := config.Load(filepath.Join(dataDir, "config.json"))
	if err != nil {
		return err
	}
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		return err
	}
	address := listener.Addr().String()
	_ = listener.Close()
	cfg.Dashboard.Enabled = true
	cfg.Dashboard.Listen = address
	cfg.SystemProxy.Mode = "clear"
	cfg.Routing.Enabled = false
	if err = config.Save(filepath.Join(dataDir, "config.json"), cfg); err != nil {
		return err
	}
	directory, endpoint = dataDir, "http://"+address
	if err = mobile.Start(dataDir, on); err != nil {
		return err
	}
	for n := 0; n < 100; n++ {
		if _, err = APIRequest("GET", "/api/status", ""); err == nil {
			return nil
		}
		if !mobile.Running() {
			return errors.New("local engine stopped before readiness")
		}
		time.Sleep(100 * time.Millisecond)
	}
	mobile.Stop()
	return errors.New("local engine did not become ready")
}

func StopEngine()         { StopPackets(); mobile.Stop() }
func EngineRunning() bool { return mobile.Running() }
func ConnectionOn() bool  { return mobile.IsOn() }
func Connect()            { mobile.Connect() }
func SetOn(on bool)       { mobile.SetOn(on) }

// APIRequest stays on authenticated loopback. It does not expose the local
// session token through the app-extension messaging interface.
func APIRequest(method, path, body string) (string, error) {
	if !strings.HasPrefix(path, "/api/") {
		return "", errors.New("invalid local API path")
	}
	if method != "GET" && method != "POST" && method != "PUT" && method != "DELETE" {
		return "", errors.New("invalid method")
	}
	token, err := os.ReadFile(filepath.Join(directory, "local-api.token"))
	if err != nil {
		return "", err
	}
	req, err := http.NewRequest(method, endpoint+path, bytes.NewBufferString(body))
	if err != nil {
		return "", err
	}
	req.Header.Set("X-Local-Token", strings.TrimSpace(string(token)))
	req.Header.Set("Content-Type", "application/json")
	transport := &http.Transport{Proxy: nil}
	defer transport.CloseIdleConnections()
	client := &http.Client{Transport: transport, Timeout: 65 * time.Second, CheckRedirect: func(_ *http.Request, _ []*http.Request) error { return http.ErrUseLastResponse }}
	response, err := client.Do(req)
	if err != nil {
		return "", err
	}
	defer response.Body.Close()
	data, err := io.ReadAll(io.LimitReader(response.Body, 4<<20+1))
	if err != nil {
		return "", err
	}
	if len(data) > 4<<20 {
		return "", errors.New("local response too large")
	}
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return "", fmt.Errorf("local request failed (HTTP %d)", response.StatusCode)
	}
	return string(data), nil
}

type packetBridge struct {
	stack  *stack.Stack
	link   *channel.Endpoint
	ctx    context.Context
	cancel context.CancelFunc
	dns    *tun.FakeDNS
}

// StartPackets exposes TCP and local DNS through the engine's SOCKS endpoint.
// Other UDP has no forwarding handler. No alternate direct route is used.
func StartPackets() error {
	StopPackets()
	if !mobile.Running() {
		return errors.New("engine is not running")
	}
	address := mobile.ProxyAddress()
	for n := 0; address == "" && n < 100 && mobile.Running() && mobile.IsOn(); n++ {
		time.Sleep(100 * time.Millisecond)
		address = mobile.ProxyAddress()
	}
	if address == "" {
		return errors.New("proxy is not ready")
	}
	dial, err := tun.SOCKS5Dialer(address, "", "")
	if err != nil {
		return err
	}
	bridge, err := newPacketBridge(dial)
	if err != nil {
		return err
	}
	packetsMu.Lock()
	packets = bridge
	packetsMu.Unlock()
	return nil
}
func StopPackets() {
	packetsMu.Lock()
	p := packets
	packets = nil
	packetsMu.Unlock()
	if p != nil {
		p.cancel()
		p.link.Close()
		p.stack.Close()
		p.stack.Wait()
	}
}
func WritePacket(data []byte) error {
	if len(data) < 20 || len(data) > 65535 {
		return errors.New("invalid IP packet length")
	}
	packetsMu.RLock()
	defer packetsMu.RUnlock()
	p := packets
	if p == nil {
		return errors.New("packet tunnel is stopped")
	}
	var protocol tcpip.NetworkProtocolNumber
	switch data[0] >> 4 {
	case 4:
		protocol = ipv4.ProtocolNumber
	case 6:
		if len(data) < 40 {
			return errors.New("short IPv6 packet")
		}
		protocol = ipv6.ProtocolNumber
	default:
		return errors.New("unsupported packet version")
	}
	packet := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(append([]byte(nil), data...))})
	defer packet.DecRef()
	p.link.InjectInbound(protocol, packet)
	return nil
}
func ReadPacket() []byte {
	packetsMu.RLock()
	p := packets
	packetsMu.RUnlock()
	if p == nil {
		return nil
	}
	packet := p.link.ReadContext(p.ctx)
	if packet == nil {
		return nil
	}
	defer packet.DecRef()
	value := packet.ToView()
	defer value.Release()
	return append([]byte(nil), value.AsSlice()...)
}
func newPacketBridge(dial tun.Dialer) (*packetBridge, error) {
	s := stack.New(stack.Options{NetworkProtocols: []stack.NetworkProtocolFactory{ipv4.NewProtocol, ipv6.NewProtocol}, TransportProtocols: []stack.TransportProtocolFactory{tcp.NewProtocol, udp.NewProtocol, icmp.NewProtocol4, icmp.NewProtocol6}})
	ctx, cancel := context.WithCancel(context.Background())
	p := &packetBridge{s, channel.New(512, 1500, ""), ctx, cancel, tun.NewFakeDNS()}
	fail := func(e tcpip.Error) (*packetBridge, error) {
		cancel()
		s.Close()
		return nil, fmt.Errorf("packet stack: %s", e)
	}
	if e := s.CreateNIC(1, p.link); e != nil {
		return fail(e)
	}
	if e := s.SetPromiscuousMode(1, true); e != nil {
		return fail(e)
	}
	if e := s.SetSpoofing(1, true); e != nil {
		return fail(e)
	}
	s.SetRouteTable([]tcpip.Route{{Destination: header.IPv4EmptySubnet, NIC: 1}, {Destination: header.IPv6EmptySubnet, NIC: 1}})
	forward := tcp.NewForwarder(s, 0, 128, func(request *tcp.ForwarderRequest) { p.forwardTCP(request, dial) })
	s.SetTransportProtocolHandler(tcp.ProtocolNumber, forward.HandlePacket)
	datagrams := udp.NewForwarder(s, func(request *udp.ForwarderRequest) bool {
		if request.ID().LocalPort != 53 {
			return false
		}
		var queue waiter.Queue
		ep, e := request.CreateEndpoint(&queue)
		if e != nil {
			return true
		}
		go func() {
			conn := gonet.NewUDPConn(&queue, ep)
			defer conn.Close()
			buf := make([]byte, 1500)
			for {
				_ = conn.SetReadDeadline(time.Now().Add(30 * time.Second))
				n, e := conn.Read(buf)
				if e != nil {
					return
				}
				reply, e := p.dns.Answer(buf[:n])
				if e == nil {
					if _, e = conn.Write(reply); e != nil {
						return
					}
				}
			}
		}()
		return true
	})
	s.SetTransportProtocolHandler(udp.ProtocolNumber, datagrams.HandlePacket)
	return p, nil
}
func (p *packetBridge) forwardTCP(request *tcp.ForwarderRequest, dial tun.Dialer) {
	id := request.ID()
	address, ok := netip.AddrFromSlice(id.LocalAddress.AsSlice())
	if !ok {
		request.Complete(true)
		return
	}
	destination := netip.AddrPortFrom(address, id.LocalPort)
	if destination.Port() == 53 {
		var queue waiter.Queue
		ep, e := request.CreateEndpoint(&queue)
		if e != nil {
			request.Complete(true)
			return
		}
		request.Complete(false)
		go func() {
			conn := gonet.NewTCPConn(&queue, ep)
			defer conn.Close()
			_ = conn.SetDeadline(time.Now().Add(30 * time.Second))
			_ = p.dns.ServeStream(conn)
		}()
		return
	}
	if tun.IsVPNAddress(address) || tun.IsFilterAddress(address) {
		request.Complete(true)
		return
	}
	target, ok := p.dns.Target(destination)
	if !ok {
		request.Complete(true)
		return
	}
	go func() {
		ctx, cancel := context.WithTimeout(p.ctx, 20*time.Second)
		defer cancel()
		upstream, err := dial(ctx, target)
		if err != nil {
			request.Complete(true)
			return
		}
		var queue waiter.Queue
		ep, e := request.CreateEndpoint(&queue)
		if e != nil {
			request.Complete(true)
			_ = upstream.Close()
			return
		}
		request.Complete(false)
		tun.Relay(gonet.NewTCPConn(&queue, ep), upstream)
	}()
}

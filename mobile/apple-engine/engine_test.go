package appleengine

import (
	"context"
	"encoding/binary"
	"errors"
	"io"
	"net"
	"net/netip"
	"testing"
	"time"

	"github.com/sagernet/gvisor/pkg/buffer"
	"github.com/sagernet/gvisor/pkg/tcpip"
	"github.com/sagernet/gvisor/pkg/tcpip/adapters/gonet"
	"github.com/sagernet/gvisor/pkg/tcpip/header"
	"github.com/sagernet/gvisor/pkg/tcpip/link/channel"
	"github.com/sagernet/gvisor/pkg/tcpip/network/ipv4"
	"github.com/sagernet/gvisor/pkg/tcpip/stack"
	"github.com/sagernet/gvisor/pkg/tcpip/transport/tcp"
	"golang.org/x/net/dns/dnsmessage"
)

func dnsQuery(t *testing.T) []byte {
	t.Helper()
	name, err := dnsmessage.NewName("example.invalid.")
	if err != nil {
		t.Fatal(err)
	}
	query, err := (&dnsmessage.Message{Header: dnsmessage.Header{ID: 85, RecursionDesired: true}, Questions: []dnsmessage.Question{{Name: name, Type: dnsmessage.TypeA, Class: dnsmessage.ClassINET}}}).Pack()
	if err != nil {
		t.Fatal(err)
	}
	return query
}
func publish(t *testing.T, p *packetBridge) {
	t.Helper()
	packetsMu.Lock()
	packets = p
	packetsMu.Unlock()
	t.Cleanup(StopPackets)
}
func udpPacket(payload []byte, port uint16) []byte {
	packet := make([]byte, 28+len(payload))
	packet[0] = 0x45
	packet[8] = 64
	packet[9] = 17
	binary.BigEndian.PutUint16(packet[2:4], uint16(len(packet)))
	copy(packet[12:16], []byte{10, 0, 0, 2})
	copy(packet[16:20], []byte{172, 19, 0, 2})
	binary.BigEndian.PutUint16(packet[20:22], 12345)
	binary.BigEndian.PutUint16(packet[22:24], port)
	binary.BigEndian.PutUint16(packet[24:26], uint16(8+len(payload)))
	copy(packet[28:], payload)
	sum := uint32(0)
	for i := 0; i < 20; i += 2 {
		sum += uint32(binary.BigEndian.Uint16(packet[i : i+2]))
	}
	for sum>>16 != 0 {
		sum = (sum & 0xffff) + (sum >> 16)
	}
	binary.BigEndian.PutUint16(packet[10:12], ^uint16(sum))
	return packet
}
func TestDNSPacketsAreAnsweredLocally(t *testing.T) {
	p, err := newPacketBridge(func(context.Context, string) (net.Conn, error) {
		t.Error("DNS must not dial upstream")
		return nil, errors.New("unexpected dial")
	})
	if err != nil {
		t.Fatal(err)
	}
	publish(t, p)
	if err = WritePacket(udpPacket(dnsQuery(t), 53)); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	reply := p.link.ReadContext(ctx)
	if reply == nil {
		t.Fatal("missing DNS response")
	}
	defer reply.DecRef()
	view := reply.ToView()
	defer view.Release()
	raw := view.AsSlice()
	var answer dnsmessage.Message
	if len(raw) < 28 {
		t.Fatal("short response")
	}
	if err = answer.Unpack(raw[int(raw[0]&15)*4+8:]); err != nil {
		t.Fatal(err)
	}
	if !answer.Response || answer.ID != 85 || len(answer.Answers) != 1 {
		t.Fatal("unexpected DNS answer")
	}
	if body, ok := answer.Answers[0].Body.(*dnsmessage.AResource); !ok || body.A[0] != 198 || body.A[1] != 18 {
		t.Fatal("expected synthetic DNS address")
	}
}
func TestOtherUDPNeverDialsTheProxy(t *testing.T) {
	called := make(chan string, 1)
	p, err := newPacketBridge(func(_ context.Context, target string) (net.Conn, error) {
		called <- target
		return nil, errors.New("unexpected dial")
	})
	if err != nil {
		t.Fatal(err)
	}
	publish(t, p)
	if err = WritePacket(udpPacket([]byte("not supported"), 443)); err != nil {
		t.Fatal(err)
	}
	select {
	case <-called:
		t.Fatal("arbitrary UDP was forwarded")
	case <-time.After(50 * time.Millisecond):
	}
}
func TestStoppingUnblocksPacketReader(t *testing.T) {
	p, err := newPacketBridge(func(context.Context, string) (net.Conn, error) { return nil, errors.New("unused") })
	if err != nil {
		t.Fatal(err)
	}
	publish(t, p)
	finished := make(chan []byte, 1)
	go func() { finished <- ReadPacket() }()
	StopPackets()
	select {
	case data := <-finished:
		if len(data) != 0 {
			t.Fatal("unexpected packet")
		}
	case <-time.After(time.Second):
		t.Fatal("reader did not stop")
	}
}
func TestMalformedPacketsAreRejected(t *testing.T) {
	for _, packet := range [][]byte{nil, make([]byte, 19), append([]byte{0x70}, make([]byte, 30)...), append([]byte{0x60}, make([]byte, 20)...), make([]byte, 65536)} {
		if WritePacket(packet) == nil {
			t.Fatal("invalid packet accepted")
		}
	}
}
func TestOnlyLocalAPIPathsAreAllowed(t *testing.T) {
	if _, err := APIRequest("GET", "https://example.invalid/", ""); err == nil {
		t.Fatal("remote URL accepted")
	}
	if _, err := APIRequest("CONNECT", "/api/status", ""); err == nil {
		t.Fatal("unsupported method accepted")
	}
}

// Two in-memory IP stacks simulate packetFlow. No external socket, account,
// Apple entitlement or Mozilla connection is involved.
func TestTCPUsesSelectedProxyAndPreservesDNSName(t *testing.T) {
	targets := make(chan string, 1)
	p, err := newPacketBridge(func(_ context.Context, target string) (net.Conn, error) {
		targets <- target
		local, remote := net.Pipe()
		go func() {
			defer remote.Close()
			request := make([]byte, 4)
			if _, e := io.ReadFull(remote, request); e == nil && string(request) == "ping" {
				_, _ = remote.Write([]byte("pong"))
			}
		}()
		return local, nil
	})
	if err != nil {
		t.Fatal(err)
	}
	publish(t, p)
	if _, err = p.dns.Answer(dnsQuery(t)); err != nil {
		t.Fatal(err)
	}
	client := stack.New(stack.Options{NetworkProtocols: []stack.NetworkProtocolFactory{ipv4.NewProtocol}, TransportProtocols: []stack.TransportProtocolFactory{tcp.NewProtocol}})
	defer client.Close()
	link := channel.New(128, 1500, "")
	defer link.Close()
	if e := client.CreateNIC(1, link); e != nil {
		t.Fatal(e)
	}
	address := tcpip.AddrFrom4([4]byte{10, 0, 0, 2})
	if e := client.AddProtocolAddress(1, tcpip.ProtocolAddress{Protocol: ipv4.ProtocolNumber, AddressWithPrefix: tcpip.AddressWithPrefix{Address: address, PrefixLen: 24}}, stack.AddressProperties{}); e != nil {
		t.Fatal(e)
	}
	client.SetRouteTable([]tcpip.Route{{Destination: header.IPv4EmptySubnet, NIC: 1}})
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	relay := func(from, to *channel.Endpoint) {
		for {
			packet := from.ReadContext(ctx)
			if packet == nil {
				return
			}
			view := packet.ToView()
			raw := append([]byte(nil), view.AsSlice()...)
			view.Release()
			packet.DecRef()
			copy := stack.NewPacketBuffer(stack.PacketBufferOptions{Payload: buffer.MakeWithData(raw)})
			to.InjectInbound(ipv4.ProtocolNumber, copy)
			copy.DecRef()
		}
	}
	go relay(link, p.link)
	go relay(p.link, link)
	destination := netip.MustParseAddr("198.18.0.1").As4()
	conn, err := gonet.DialContextTCP(ctx, client, tcpip.FullAddress{NIC: 1, Addr: tcpip.AddrFrom4(destination), Port: 443}, ipv4.ProtocolNumber)
	if err != nil {
		t.Fatal(err)
	}
	defer conn.Close()
	_ = conn.SetDeadline(time.Now().Add(time.Second))
	if _, err = conn.Write([]byte("ping")); err != nil {
		t.Fatal(err)
	}
	reply := make([]byte, 4)
	if _, err = io.ReadFull(conn, reply); err != nil {
		t.Fatal(err)
	}
	if string(reply) != "pong" {
		t.Fatal("unexpected proxied response")
	}
	select {
	case target := <-targets:
		if target != "example.invalid:443" {
			t.Fatalf("target %q", target)
		}
	case <-ctx.Done():
		t.Fatal("no proxy dial")
	}
}

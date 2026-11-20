package tun

import (
	"context"
	"errors"
	"io"
	"net"
	"sync"
	"time"

	"golang.org/x/net/proxy"
)

// Dialer opens a connection to target ("host:port") through the proxy.
type Dialer func(ctx context.Context, target string) (net.Conn, error)

// Allow reports whether a connection to host:port is sent to the proxy at
// all; one it refuses is reset at once, without being tried.
type Allow func(host, port string) bool

// dialTimeout bounds connecting through the proxy, which may first wake
// and connect to Mozilla.
const dialTimeout = 30 * time.Second

// SOCKS5Dialer dials through the SOCKS5 proxy at addr, with its login if
// user is not empty.
func SOCKS5Dialer(addr, user, password string) (Dialer, error) {
	var auth *proxy.Auth
	if user != "" {
		auth = &proxy.Auth{User: user, Password: password}
	}
	d, err := proxy.SOCKS5("tcp", addr, auth, &net.Dialer{Timeout: dialTimeout})
	if err != nil {
		return nil, err
	}
	cd, ok := d.(proxy.ContextDialer)
	if !ok {
		return nil, errors.New("the SOCKS5 dialer cannot take a context")
	}
	return func(ctx context.Context, target string) (net.Conn, error) {
		conn, err := cd.DialContext(ctx, "tcp", target)
		if err == nil {
			if tcpConn, ok := conn.(*net.TCPConn); ok {
				_ = tcpConn.SetNoDelay(true)
				_ = tcpConn.SetReadBuffer(4 * 1024 * 1024)
				_ = tcpConn.SetWriteBuffer(4 * 1024 * 1024)
			}
		}
		return conn, err
	}, nil
}

// closeWriter is a connection that can end its sending half.
type closeWriter interface{ CloseWrite() error }

const relayBufferSize = 512 * 1024

var relayBufferPool = sync.Pool{
	New: func() any {
		return make([]byte, relayBufferSize)
	},
}

// Relay copies between a and b until both directions end, passing on each
// end of stream as a half close, then closes both.
func Relay(a, b net.Conn) {
	if tcpA, ok := a.(*net.TCPConn); ok {
		_ = tcpA.SetNoDelay(true)
		_ = tcpA.SetReadBuffer(4 * 1024 * 1024)
		_ = tcpA.SetWriteBuffer(4 * 1024 * 1024)
	}
	if tcpB, ok := b.(*net.TCPConn); ok {
		_ = tcpB.SetNoDelay(true)
		_ = tcpB.SetReadBuffer(4 * 1024 * 1024)
		_ = tcpB.SetWriteBuffer(4 * 1024 * 1024)
	}
	var wg sync.WaitGroup
	copyHalf := func(dst, src net.Conn) {
		defer wg.Done()
		buf := relayBufferPool.Get().([]byte)
		defer relayBufferPool.Put(buf)
		_, _ = io.CopyBuffer(dst, src, buf)
		if cw, ok := dst.(closeWriter); ok {
			_ = cw.CloseWrite()
		} else {
			_ = dst.Close()
		}
	}
	wg.Add(2)
	go copyHalf(a, b)
	go copyHalf(b, a)
	wg.Wait()
	_ = a.Close()
	_ = b.Close()
}

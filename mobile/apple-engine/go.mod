module github.com/III85III/III85III-VPN/core/appleengine

go 1.27.2

require (
	github.com/III85III/III85III-VPN/core v0.0.0
	github.com/sagernet/gvisor v0.0.0-20260727.0-sing-box-mod.1
	golang.org/x/net v0.59.0
)

require (
	github.com/google/btree v1.1.3 // indirect
	github.com/quic-go/qpack v0.6.0 // indirect
	github.com/quic-go/quic-go v0.59.0 // indirect
	golang.org/x/crypto v0.57.0 // indirect
	golang.org/x/exp v0.0.0-20250711185948-6ae5c78190dc // indirect
	golang.org/x/mobile v0.0.0-20260908204917-8b95e45f8d3e // indirect
	golang.org/x/mod v0.41.0 // indirect
	golang.org/x/sync v0.23.0 // indirect
	golang.org/x/sys v0.48.0 // indirect
	golang.org/x/text v0.42.0 // indirect
	golang.org/x/time v0.15.0 // indirect
	golang.org/x/tools v0.51.0 // indirect
	gvisor.dev/gvisor v0.0.0-20261014183808-7e3f1b18dc2d // indirect
)

replace github.com/III85III/III85III-VPN/core => ../../core

tool (
	golang.org/x/mobile/cmd/gobind
	golang.org/x/mobile/cmd/gomobile
)

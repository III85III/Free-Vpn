<div align="center">

**🇺🇸 English** &nbsp;/&nbsp; [🇮🇷 فارسی](README.fa.md)

</div>
<br>

<p align="center">
  <img src="docs/images/header-banner.jpg" alt="III85III VPN Luxury Header Banner" width="100%" style="border-radius: 12px; max-width: 900px;" />
</p>

<h1 align="center">III85III VPN</h1>

<h3 align="center">Next-Generation Anti-Censorship Tunnel Powered by Mozilla Global Infrastructure</h3>

<div align="center">

[![Latest Release](https://img.shields.io/github/v/release/III85III/Free-Vpn?style=flat-square&color=D9BD83&label=Release)](https://github.com/III85III/Free-Vpn/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/III85III/Free-Vpn/total?style=flat-square&color=D9BD83&logo=github&label=Downloads)](https://github.com/III85III/Free-Vpn/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Android%20%7C%20macOS-101319?style=flat-square&logo=windows&logoColor=D9BD83)](https://github.com/III85III/Free-Vpn/releases/latest)
[![License](https://img.shields.io/badge/License-GPL--3.0-101319?style=flat-square&color=D9BD83)](LICENSE)
[![Telegram](https://img.shields.io/badge/Telegram-@cabicy-2CA5E0?style=flat-square&logo=telegram&logoColor=white)](https://t.me/cabicy)

</div>

---

<h3 align="center">⚠️ Independent Project Disclaimer</h3>

<p align="center">
<strong>III85III VPN</strong> is an independent open-source project. It is <strong>not</strong> manufactured, owned, sponsored, or endorsed by Mozilla Corporation, and is not officially affiliated with Mozilla in any way. For full legal and operational details, see the <a href="#-disclaimer--terms">Disclaimer section</a>.
</p>

---

## 💎 What is III85III VPN?

**III85III VPN** is a free, high-performance, ad-free virtual private network client engineered to circumvent extreme censorship, deep packet inspection (DPI), and national network blackouts.

By carrying encrypted traffic over **HTTP/1.1, HTTP/2, and HTTP/3 (QUIC)** tunnels terminating at **Mozilla's global edge infrastructure on Fastly CDN**, III85III VPN makes your network traffic virtually indistinguishable from standard, legitimate web browsing.

Designed from the ground up for **Windows, Android, and macOS**, featuring an ultra-clean **Obsidian Jet-Black & Liquid Gold** user interface with zero advertisements and zero tracking.

---

## 🚀 Key Features

* ⚡ **Multi-Protocol Engine:** High-speed tunneling over HTTP/1.1, HTTP/2, and HTTP/3 over QUIC for optimal throughput and low latency.
* 🦊 **Firefox uTLS Camouflage:** Connections to edge endpoints match Mozilla Firefox's genuine TLS Client Hello fingerprint, evading SNI-based filtering and active network probing.
* 🛡️ **Anti-Censorship Suite:** Built-in TLS SNI packet fragmentation, domain fronting, Tor Snowflake fallback, clean IP scanner, and Port 53 DNS tunneling for complete blackout resilience.
* 💎 **Instant One-Tap Connect:** Includes pre-provisioned, high-capacity account pools (~50 GB/month per Mozilla account) with automatic, seamless rotation upon quota exhaustion.
* 👤 **Personal Account Support:** Freely connect your own personal Mozilla / Firefox account credentials directly inside the app.
* 📱 **Telegram Direct Proxy:** Embedded 24/7 background MTProto/Socks5 proxy for instant Telegram messaging without full-device tunnel battery drain.
* 🔀 **Smart Routing (Split Tunneling):** Exclude banking applications, local government services, or specific programs from the tunnel while keeping international traffic protected.
* 🔒 **Encrypted DNS & Ad Blocker:** Built-in DNS-over-HTTPS (DoH) with zero logging, blocking ads, trackers, and malicious domains across all network locations.
* 🚫 **100% Ad-Free & Privacy First:** Zero banner ads, zero interstitial popups, zero user tracking, and no external tracking telemetry.
* 🌙 **Luxury Precision Interface:** Handcrafted Obsidian & Liquid Gold minimalist design with real-time speed monitors, ping tests, and responsive layout.
* 🔄 **Autonomous In-App Updates:** Integrated update verification through GitHub Releases ensures immediate access to the latest anti-filtering patches.

---

## 📥 Download Matrix

| Platform | Recommended File | Architecture / Type | Direct Download |
| :--- | :--- | :--- | :--- |
| **Windows** | `III85III-VPN-Setup.exe` | Windows 10/11 (64-bit) • Standard Installer | [Download Setup (.exe)](https://github.com/III85III/Free-Vpn/releases/latest/download/III85III-VPN-Setup.exe) |
| **Windows Portable** | `III85III-VPN-v1.0.0-windows-x64.zip` | Windows 10/11 (64-bit) • Portable (No install) | [Download ZIP](https://github.com/III85III/Free-Vpn/releases/latest/download/III85III-VPN-v1.0.0-windows-x64.zip) |
| **Android** | `III85III-VPN.apk` | Android 8.0+ • Universal (ARM64 & x86_64) | [Download APK (.apk)](https://github.com/III85III/Free-Vpn/releases/latest/download/III85III-VPN.apk) |
| **macOS** | `III85III-VPN-macOS.dmg` | macOS 12+ (Apple Silicon & Intel) | [Download DMG (.dmg)](https://github.com/III85III/Free-Vpn/releases/latest/download/III85III-VPN-macOS.dmg) |

### Which file should I download?

* **Windows:** Download the **Setup (.exe)** installer for automatic desktop shortcuts, Start Menu integration, and silent auto-updates. Choose the **Portable (.zip)** version if you want to run the client from a flash drive without administrative installation.
* **Android:** Download the **APK** directly to your phone. It installs on all modern Android devices running Android 8.0 or newer (phones, tablets, and Android TV).
* **macOS:** Download the **DMG** file, open it, and drag the application into your `Applications` folder.

---

## ⚙️ Architecture & How It Works

III85III VPN employs an advanced multi-layered proxy architecture to deliver secure internet access under heavy network restrictions:

```
[ Your Device ]
       │
       ▼  (Firefox uTLS Handshake + SNI Fragmentation)
[ Fastly Edge CDN Infrastructure ]
       │
       ▼  (Authenticated HTTP/2 & HTTP/3 Secure Tunnel)
[ Mozilla Global Gateway Servers ]
       │
       ▼  (High-Speed Egress)
[ Open Global Internet ]
```

1. **Camouflage:** Client initiates an encrypted TLS session that exactly replicates Mozilla Firefox's cipher suites, extensions, and ALPN negotiate parameters.
2. **Fastly Ingress:** Traffic enters Fastly's global edge network, effectively disguising the connection as ordinary encrypted web browsing.
3. **Internal Relay:** Edge nodes hand off encrypted stream frames to Mozilla's backend egress nodes.
4. **Target Destination:** Mozilla gateways route traffic to the destination web server, hiding your original IP and geographic location.

---

## 🛡️ Censorship Circumvention Suite

When internet access is restricted or throttled by regional internet service providers:

* **TLS Fragmentation:** Splits the Client Hello packet across multiple TCP segments to bypass Deep Packet Inspection (DPI) boxes that filter based on Plaintext SNI headers.
* **Tor Snowflake Bridge:** Seamless fallback transport that routes traffic through volunteer WebRTC proxies when direct CDN IP ranges are throttled.
* **Clean IP Scanner:** Tests and identifies reachable CDN edge IP addresses with minimal latency during regional network disruptions.
* **Port 53 DNS Tunneling:** Fallback mechanism allowing crucial handshake metadata to pass through open UDP DNS ports during partial internet blackouts.

---

## 📋 Known Mozilla Network Limitations

To maintain network integrity, Mozilla's gateway infrastructure enforces specific protocol rules:

* **Supported TCP Ports:** `80`, `443`, `8080`, and `8443`. Non-web ports (such as standard mail ports `25`, `465`, `587`, `993` or direct SSH port `22`) are restricted. (Note: Git operations over SSH via port `443` operate normally).
* **Supported UDP Ports:** `80`, `443`, `3478`, `8080`, `8443`, and `49152-65534` (WebRTC voice/video calls and modern browser traffic).
* **Service Allowance:** Free Mozilla accounts provide ~50 GB bandwidth per month. III85III VPN includes automatic account replenishment and multi-account rotation so your connectivity is never interrupted.

---

## 🔒 Disclaimer & Terms

* **Unofficial Client:** III85III VPN is an independent development effort and is not affiliated with, sponsored by, or endorsed by Mozilla Corporation.
* **Account Credentials:** When you use your own Mozilla account, your credentials and session tokens remain stored securely on your local device only and are never transmitted to any third-party server.
* **Service Availability:** Network speeds, node availability, and quota allowances are subject to Mozilla's and Fastly's infrastructure policies.

---

## 💬 Community & Support

For server status updates, assistance, or feedback:
* **Telegram Channel:** [@cabicy](https://t.me/cabicy)
* **GitHub Issues:** [Report a Bug or Request a Feature](https://github.com/III85III/Free-Vpn/issues)

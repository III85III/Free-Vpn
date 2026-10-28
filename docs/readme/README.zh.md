<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · [🇩🇪 Deutsch](README.de.md) · [🇫🇷 Français](README.fr.md) · [🇪🇸 Español](README.es.md) · [🇧🇷 Português](README.pt.md) · [🇷🇺 Русский](README.ru.md) · **🇨🇳 简体中文** · [🇯🇵 日本語](README.ja.md) · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**使用您自己的 Mozilla 账户的免费 Windows 客户端**

III85III VPN 是面向符合服务资格的 Mozilla 账户的本地客户端。无需自备 VPS；流量通过 Mozilla 服务的远程基础设施传输。

> **开发预览版** — 真实 Mozilla 登录和所选应用的出口仍需实际验证。这不是稳定版。客户端免费不代表无限流量或保证获得免费服务；资格、地区和配额由 Mozilla 决定。

## 功能

- 仅所选应用使用 VPN 出口，其他应用直连。
- 手动切换多个账户，使用 Windows DPAPI 保护本地会话，各账户配额独立。
- 收藏及最近服务器、延迟排序和退避重连；手动断开或账户/配额被拒绝后停止重试。
- 托盘菜单、波斯语/英语界面、明暗主题，以及不含账户标识的诊断摘要。

## 下载与开始

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. 从 Releases 下载并运行 III85III-VPN-Setup.exe；安装程序会自动解压所需组件。
2. 运行 III85III VPN.exe；程序启动时不自动连接。
3. 添加您自己的 Mozilla 账户，并亲自完成验证。请勿在程序中输入 Google 密码。
4. 选择并保存应用，选择服务器，然后连接；如出现 Windows 路由权限请求，请检查确认。

## 限制与隐私

仅支持端口 80、443、8080、8443 的网页 TCP；所选应用的 UDP 被拒绝。DNS、IPv6、实际出口及故障恢复仍待实测。下载中不包含开发者账户、邮箱或会话，账户库不保存密码。

## 验证与计划

已通过 25 项 Node 测试、44 项桌面核心检查和 891 项布局检查。这些不能替代真实连接测试。Android、iOS、macOS、Linux 及应用商店发布均为未来计划。

[完整指南与源码构建](../OVERVIEW.md#build-from-source) · [Privacy](../../PRIVACY.md) · [Validation](../../VALIDATION.md) · [Roadmap](../../ROADMAP.md) · [Protocol](../../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


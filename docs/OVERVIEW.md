<p align="center"><img src="../native/assets/brand/iii85iii-luxury.png" width="128" alt="III85III"></p>

# III85III VPN

**A free, local Windows client for eligible Mozilla accounts. No personal VPS required.**

Choose the applications that use the VPN, keep everything else direct, and manage your connection from a quiet desktop interface. English and Persian, dark and light themes, one identity: III85III.

> **Development preview — not a production VPN release.** Local protocol, gateway, desktop-interface and account-storage tests pass. Live Mozilla authentication and selected-versus-direct application egress still need owner validation. An eligible account and Mozilla's existing remote service are required. This app does not provide unlimited bandwidth, create a free service entitlement, bypass quotas, or operate an independent exit network.

## What is included

- **Selected applications only:** checked executable names route through the local gateway; other apps use the direct outbound. Selected-app UDP is rejected.
- **Connection recovery:** bounded backoff after an intentionally started connection. Manual disconnect and account/quota refusals stop retries; startup never connects automatically.
- **Locations:** Mozilla's public catalog, measured TLS latency ordering, favorites and recently used destinations. Account restrictions remain enforced by the service.
- **Multiple accounts:** manually add and select your own Mozilla accounts. Each session is stored locally with Windows DPAPI; the active account's service quota is shown separately.
- **Balance and reset:** service-reported remaining bytes and reset time. Missing information stays unknown.
- **Tray controls:** connect, disconnect, favorites, recent locations and quit.
- **Appearance:** Persian RTL / English, bundled Vazirmatn / Lato, and light / dark themes.
- **Useful details:** keyboard shortcuts, email masking and a shareable diagnostic summary that excludes account identifiers, tokens, paths and raw logs.

## Download and first connection

Windows x64 previews are published under [Releases](https://github.com/III85III/III85III-VPN/releases). Choose a **pre-release** only if you are comfortable testing unfinished software.

1. Download and run `III85III-VPN-Setup.exe`. The installer extracts the application and all required components for your Windows user.
2. Run **III85III VPN.exe**. It starts disconnected.
3. Add **your own Mozilla account** on the Account page. A Google-only sign-in may require establishing a Mozilla password; do not enter your Google password into this app. Complete any supported verification yourself.
4. Select the applications that should use VPN and save. Choose a location.
5. Connect and approve Windows' local-routing prompt if requested.

The client is free software. The remote service controls eligibility, supported regions, destinations, quotas and renewal times. No account belonging to the developer is included. Saved login remains on the same Windows user account until removed or invalidated by Mozilla.

**Supported service traffic:** web TCP ports 80, 443, 8080 and 8443. This is not a general UDP or arbitrary-port VPN. DNS/process-routing behavior and real application egress remain part of pending live validation. A local network adapter is used even though only selected applications are sent to the remote service.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| Ctrl+D | Disconnect |
| Ctrl+L | Locations and search |
| Ctrl+K | Application selection and search |

## Build from source

Requirements: Windows x64, .NET Framework 4.8 compiler, PowerShell and Node.js 24 or newer on PATH. The app ships its own Node runtime after building.

```powershell
.\tools\get-router.ps1
.\tools\get-license-sources.ps1
.\tools\test.ps1
.\tools\build.ps1
.\tools\package.ps1
.\tools\build-installer.ps1
```

The routing download comes from the official SagerNet release and its SHA-256 is checked before extraction. Dependency licenses and matching routing source accompany the downloadable archive. The build does not import a developer account or local settings.

## Privacy and implementation

The interface and independent local controller are written for III85III. Mozilla account and gateway behavior follow the [official Mozilla protocol references](../PROTOCOL.md). No executable or source archive from the previous third-party Firefox VPN Client is included in this repository or its independent preview.

The controller binds only to authenticated `127.0.0.1`. Sessions are protected using Windows DPAPI and a user-private data directory. Passwords are used during sign-in and are not written to the account vault. There is no III85III VPS or analytics backend. Mozilla and the chosen service infrastructure still receive the requests needed to authenticate and transport traffic.

See [PRIVACY.md](../PRIVACY.md), [VALIDATION.md](../VALIDATION.md) and [SECURITY.md](../SECURITY.md). Please report issues with **Copy safe report** rather than uploading account files or raw logs.

## فارسی

III85III یک برنامهٔ محلی و رایگان برای ویندوز است که با **حساب موزیلای خودتان** کار می‌کند؛ به VPS شخصی نیاز ندارید. برنامه‌های دلخواه را برای استفاده از VPN انتخاب کنید و بقیه مستقیم وصل شوند. رابط فارسی راست‌به‌چپ، حالت روشن و تیره، سرورهای محبوب، اتصال مجدد و چند حساب با انتخاب دستی در نظر گرفته شده‌اند.

این نسخه **پیش‌نمایش توسعه** است و هنوز جایگزین یک VPN نهاییِ تأییدشده نیست. ورود واقعی و مسیر خروج برنامه‌ها به تست تکمیلی نیاز دارند. رایگان‌بودن خود برنامه به معنی اینترنت یا سهمیهٔ نامحدود نیست؛ دسترسی و سهمیه را موزیلا تعیین می‌کند. هیچ ایمیل، حساب، رمز یا توکن سازنده همراه دانلود قرار ندارد.

## Roadmap and contribution

Android, iOS, macOS, Linux, Google Play and App Store distribution are **future plans**, not currently available clients or approved listings. See [ROADMAP.md](../ROADMAP.md). Fixes, accessibility feedback, translations and reproducible bug reports are welcome. Please avoid posting credentials or personal information in public issues.

Project code: MIT. Node.js, sing-box, fonts and system dependencies retain their respective licenses; see [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt). This unofficial client is not affiliated with or endorsed by Mozilla. Mozilla and Firefox names and marks belong to their owners.

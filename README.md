# ONYX // VPN

VPN client for Windows and Android with Mozilla account sign-in, server selection, connection modes and application routing.

The Windows interface uses a local glass-style dashboard with animated connection feedback, live traffic and compact mode. Account and connection information comes from the VPN engine.

## Downloads

* **[Windows Setup (.exe)](https://github.com/III85III/ONYX/releases/download/v1.1.20/ONYX-VPN-Setup.exe)**
* **[ONYX for Android](https://github.com/III85III/ONYX/releases/download/v1.1.24/ONYX-VPN.apk)**
* **[Releases](https://github.com/III85III/ONYX/releases/latest)** · **[Telegram](https://t.me/cabicy)**

The ONYX Windows interface is available as source below. Some Shield controls are disabled while their engine integration is pending.

## Source

`desktop/Onyx.Vpn.cs` contains the Windows client and its connection controls. `desktop/OnyxWindow.cs` connects the ONYX interface to those controls. Edit `desktop/interface/index.html` to change its appearance.

Run `desktop/Build.ps1` on Windows with .NET Framework 4.8. WebView2 Runtime must be installed. Build outputs go to `desktop/output`.

`core` contains the shared Go engine. `mobile/android` contains the Android client and its build instructions.

Legacy profile folders, service identifiers, Android application identifiers and update URLs remain compatibility identifiers so existing accounts and installations can be recognized. The branding is ONYX.

Mozilla controls account availability, verification and quota. The application displays the quota returned for each account.

Licensed under GPLv3. Dependency notices are included with the source.

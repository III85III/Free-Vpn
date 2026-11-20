# ONYX // VPN

VPN client for Windows and Android with Mozilla account sign-in, server selection, connection modes and application routing.

The Windows interface uses a local glass-style dashboard with animated connection feedback, live traffic and compact mode. Account and connection information comes from the VPN engine.

## Downloads

[Windows installer](https://github.com/III85III/Free-Vpn/releases/download/v1.1.20/III85III-VPN-Setup.exe) · [Android APK](https://github.com/III85III/Free-Vpn/releases/download/v1.1.23/III85III-VPN.apk) · [Telegram](https://t.me/cabicy)

These installers precede the ONYX interface update. The ONYX Windows interface is available as source below. Some Shield controls are disabled while their engine integration is pending.

## Source

`desktop/Onyx.Vpn.cs` contains the existing Windows client and its connection controls. `desktop/OnyxWindow.cs` connects the ONYX interface to those controls. Edit `desktop/interface/index.html` to change its appearance.

Run `desktop/Build.ps1` on Windows with .NET Framework 4.8. The interface uses an existing 85 VPN installation and its profile. WebView2 Runtime must be installed. Build outputs go to `desktop/output`.

`core` contains the shared Go engine. `mobile/android` contains the Android client and its build instructions.

Legacy profile folders, service identifiers, Android application identifiers and update URLs remain compatibility identifiers so existing accounts and installations can be recognized. The branding is ONYX.

Mozilla controls account availability, verification and quota. The application displays the quota returned for each account.

Licensed under GPLv3. Dependency notices are included with the source.

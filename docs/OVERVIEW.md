# Using III85III VPN

Download the Windows Setup EXE from [Releases](https://github.com/III85III/Free-Vpn/releases). It installs the interface, native engine and licenses for the current Windows user.

1. Open Account and add your own Mozilla account with its Mozilla password.
2. If Mozilla requests verification, enter the code in the separate verification window.
3. Choose a location and a connection mode, then connect.
4. In App rules, checked applications are **excluded from VPN** by the engine.

The existing working desktop version is preserved while the replacement is checked. Account data remains in the user's local data directory. No personal account or session is distributed.

This release features the customized, high-performance III85III native Go engine (`core/`). The interface and installer identify III85III as their publisher.

## Build from source

Windows x64, Go 1.27+, and the .NET Framework 4.8 compiler are required.

```powershell
.\tools\test.ps1
.\tools\build.ps1
.\tools\build-installer.ps1
```

[Privacy](PRIVACY.md) · [Validation](VALIDATION.md) · [Roadmap](ROADMAP.md) · [Security](../.github/SECURITY.md) · [Notices](../licenses/NOTICE.txt)
# Using III85III VPN

Download the Windows Setup EXE from [Releases](https://github.com/III85III/Free-Vpn/releases). It installs the interface, original engine and licenses for the current Windows user.

1. Open Account and add your own Mozilla account with its Mozilla password.
2. If Mozilla requests verification, enter the code in the separate verification window.
3. Choose a location and a connection mode, then connect.
4. In App rules, checked applications are **excluded from VPN** by the original engine.

The existing working desktop version is preserved while the replacement is checked. Account data remains in the original user's local data directory. No personal account or session is distributed.

This release restores the original Firefox VPN Client engine by Arman. It does not use the experimental Node/sing-box implementation. The engine is unchanged; the interface and installer identify III85III as their publisher. Original credits and GPL notices are retained.

## Build from source

Windows x64 and the .NET Framework 4.8 compiler are required.

```powershell
.\tools\get-engine.ps1 -EngineDirectory 'C:\path\to\original\core' -SourceArchive 'C:\path\to\upstream-source.zip'
.\tools\test.ps1
.\tools\build.ps1
.\tools\build-installer.ps1
```

Use the original Firefox VPN Client 1.0.4 components matching [the pinned hashes](../third_party/engine/manifest.json) and its upstream source snapshot. The downloaded III85III installer includes these under core/ and source/. No Node runtime is required.

[Privacy](PRIVACY.md) · [Validation](VALIDATION.md) · [Roadmap](ROADMAP.md) · [Security](../.github/SECURITY.md) · [Third-party notices](../licenses/NOTICE.txt)

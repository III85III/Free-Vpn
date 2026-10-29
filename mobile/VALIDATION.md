# Mobile build validation

## Current Android release: 2.3.2

Android 2.3.2 (code 23002) passed build, lint, unit tests and fresh Android 8/14/16 emulator checks in [run 40974953443](https://github.com/III85III/Free-Vpn/actions/runs/40974953443). This includes synthetic native TUN DNS and a user-confirmed in-app PackageInstaller update. Its UI uses shorter labels, pinned automatic server choices, immediate account-check feedback and themed error details. Production signing identity is unchanged; real-phone Mozilla sign-in, egress and background/handover remain unverified.

APK SHA-256: 26c7e29aeeb6a5dacb5dae73cd8ad622aaa15baf8e92381a9724db24cbdb0d1e.

## Previous release: 2.3.1


Android 2.3.1 at functional commit `f2482ba` passed the [complete Android build, lint and device workflow](https://github.com/III85III/Free-Vpn/actions/runs/40945840604). It uses fresh Android 8, 14 and 16 Google APIs emulators. The separate iOS 2.2.0 project passed the [earlier complete mobile workflow](https://github.com/III85III/Free-Vpn/actions/runs/40885073220). These results do not establish store approval or real-phone Mozilla account/egress behavior.

| Platform | Completed checks | Still requires a real device |
| --- | --- | --- |
| Android | Release/debug APK builds; debug/release Android Lint; 20 connection/lifecycle/update-policy unit tests; authenticated local API; empty accounts and disconnected initial state; missing-token rejection; initial sign-in form and cancellation; privacy page; portrait/landscape screenshots; real VPN interface answering synthetic DNS from a separate test-app UID; in-app PackageInstaller update with Android confirmation and preserved synthetic private data | Real Mozilla sign-in/verification and upstream egress, app exclusions, background survival, consent/revocation on physical devices and network handover |
| iOS | Six synthetic packet-adapter tests, Go framework build, Swift/Objective-C compilation, Simulator and device builds, unsigned IPA with app and extension | Valid Apple signing and capabilities, installation, account flow, packet-flow egress, extension memory/background behavior and network handover |

The Android app targets API 36, supports Android 8.0+, and includes arm64 and x86-64 native libraries. Both libraries have 16 KB ELF load alignment and aligned RELRO endpoints. ZIP alignment was checked. This does not establish testing on physical 16 KB devices. The release is not debuggable, contains no debug screenshot override, and includes privacy/font/dependency notices. Known private account/configuration/signing filenames were checked against the APK contents.

APK v2/v3 signatures were verified. Its certificate matches the previously delivered Android 2.2.0 APK, allowing same-package updates. The in-place emulator update uses two debug APKs from the same CI build and a synthetic local file; it does not test preservation of real Mozilla sessions or install the private release signing key in CI.

Android version: **2.3.1**, code **23001**. Package: `io.github.iii85iii.vpn`. Release certificate SHA-256: `c70761b3367f8e4974abedee4204756a595cd9e797121454b4bc6b53ed947108`. APK SHA-256: `0f0e657e15e1f967a88afbefd41428dd795c3fa92b348428bccb6688e3fef23f`. Private keys remain outside the repository, APK and CI.

The native TUN test uses a synthetic DNS name and fake address; it does not contact Mozilla or prove upstream traffic. The updater test stages a higher-version debug APK, checks its package/signature, invokes the app's PackageInstaller session and confirms installation through Android's own UI. It does not use the production signing key in CI. Debug-only fixture entry points are absent from the distributed APK.

Windows 2.2.1 passed the native account/preflight, verification-conflict, layout, latency, polling, keyboard, update-policy and installer-recovery suites. A fresh empty-profile smoke run against the unchanged 2.2.0 backend verified startup, loopback authentication and settings round-trips. Backend executable hashes match the preserved validated build. Actual Windows TUN installation, routing and real-account egress still need device validation. Windows installer SHA-256: `f4d0ee349fad2b500161dddd942e8a8a2b80cb59c91cb2b0a075c857f57103f6`.

The iOS app and extension use version 2.2.0, build 22000. The application uses the public Network Extension packet-flow API. The unsigned IPA contains no provisioning profile, signing key, preconfigured account or user session. It cannot be installed as an ordinary signed App Store/TestFlight app.

The emulator check refuses physical devices and is restricted to a fresh CI image. No real account was supplied to the CI jobs. Local build logs, test tokens, profiles, signing credentials, generated AAR/frameworks and APK/IPA outputs are excluded from source publication.

Store preparation remains separate: developer accounts, signing/provisioning, privacy and VPN disclosures, store-specific update mechanisms and real-device QA must be completed before submission. The unsigned iOS artifact remains separate from the Windows and Android direct downloads.

# Mobile build validation

Android 2.3.0 is distributed directly as a signed APK. The [Android build and lint run](https://github.com/III85III/Free-Vpn/actions/runs/40907281877) passed; the [device checks](https://github.com/III85III/Free-Vpn/actions/runs/40911148767) use fresh Google APIs emulators. Android app code is unchanged from commit `4119687`; later commits improve the test harness and documentation. The separate iOS 2.2.0 project passed the [earlier complete mobile workflow](https://github.com/III85III/Free-Vpn/actions/runs/40885073220). These results do not establish store approval or real-phone account/tunnel behavior.

| Platform | Completed checks | Still requires a real device |
| --- | --- | --- |
| Android | Release/debug APK builds; debug/release Android Lint; 14 connection-truth/lifecycle unit tests; fresh Android 8, 14 and 16 x86-64 emulator startup; authenticated local API; empty accounts and disconnected initial state; missing-token rejection; email/password-only initial form and cancellation; privacy page; portrait/landscape screenshots; higher-version in-place debug update retaining a non-secret private-file probe | Mozilla sign-in and verification, actual tunneled egress, app exclusions, background operation, VPN consent/revocation and network handover |
| iOS | Six synthetic packet-adapter tests, Go framework build, Swift/Objective-C compilation, Simulator and device builds, unsigned IPA with app and extension | Valid Apple signing and capabilities, installation, account flow, packet-flow egress, extension memory/background behavior and network handover |

The Android app targets API 36, supports Android 8.0+, and includes arm64 and x86-64 native libraries. Both libraries have 16 KB ELF load alignment and aligned RELRO endpoints. ZIP alignment was checked. This does not establish testing on physical 16 KB devices. The release is not debuggable, contains no debug screenshot override, and includes privacy/font/dependency notices. Known private account/configuration/signing filenames were checked against the APK contents.

APK v2/v3 signatures were verified. Its certificate matches the previously delivered Android 2.2.0 APK, allowing same-package updates. The in-place emulator update uses two debug APKs from the same CI build and a synthetic local file; it does not test preservation of real Mozilla sessions or install the private release signing key in CI.

Android version: **2.3.0**, code **23000**. Package: `io.github.iii85iii.vpn`. Release certificate SHA-256: `c70761b3367f8e4974abedee4204756a595cd9e797121454b4bc6b53ed947108`. APK SHA-256: `9908c3fd6761d0dc46a2ad0b35e9d59a6b8c9894ccad9a9ea340ba8e47d5b214`. Private keys remain outside the repository, APK and CI.

The iOS app and extension use version 2.2.0, build 22000. The application uses the public Network Extension packet-flow API. The unsigned IPA contains no provisioning profile, signing key, preconfigured account or user session. It cannot be installed as an ordinary signed App Store/TestFlight app.

The emulator check refuses physical devices and is restricted to a fresh CI image. No real account was supplied to the CI jobs. Local build logs, test tokens, profiles, signing credentials, generated AAR/frameworks and APK/IPA outputs are excluded from source publication.

Store preparation remains separate: current target/API and packaging requirements, developer accounts, signing/provisioning, privacy and VPN disclosures, release metadata and real-device QA must be completed before submission. The production Windows release remains separate from these mobile development artifacts.

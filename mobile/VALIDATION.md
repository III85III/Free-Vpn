# Mobile build validation

The native mobile code at commit `3f1644e` passed the [complete mobile workflow](https://github.com/III85III/Free-Vpn/actions/runs/40885073220). These are development builds for testing, not approved store releases.

| Platform | Completed checks | Still requires a real device |
| --- | --- | --- |
| Android | APK build, Android Lint, eight connection-state unit tests, native startup on a freshly wiped Android 14 x86-64 emulator, authenticated local API, empty accounts, disconnected initial state and unauthenticated account-API rejection | Mozilla sign-in and verification, actual tunneled egress, app exclusions, background operation, revocation and network handover |
| iOS | Six synthetic packet-adapter tests, Go framework build, Swift/Objective-C compilation, Simulator and device builds, unsigned IPA with app and extension | Valid Apple signing and capabilities, installation, account flow, packet-flow egress, extension memory/background behavior and network handover |

The Android app targets API 34, supports Android 8.0+, and includes arm64 and x86-64 native libraries. Both libraries were rebuilt with 16 KB ELF load alignment; their RELRO endpoints were also checked. This is not a claim that every 16 KB phone or store requirement has been tested. APK signing uses an III85III identity; the private key stays outside the repository and application.

The iOS app and extension use version 2.2.0, build 22000. The application uses the public Network Extension packet-flow API. The unsigned IPA contains no provisioning profile, signing key, preconfigured account or user session. It cannot be installed as an ordinary signed App Store/TestFlight app.

The emulator check refuses physical devices and is restricted to a fresh CI image. No real account was supplied to the CI jobs. Local build logs, test tokens, profiles, signing credentials, generated AAR/frameworks and APK/IPA outputs are excluded from source publication.

Store preparation remains separate: current target/API and packaging requirements, developer accounts, signing/provisioning, privacy and VPN disclosures, release metadata and real-device QA must be completed before submission. The production Windows release remains separate from these mobile development artifacts.

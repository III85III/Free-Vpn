# III85III VPN for mobile

Native Android and iOS interfaces with the desktop application's dark palette, warm gold controls, location search, account management and separate verification-code step. The existing Mozilla account protocol and service quotas are unchanged.

## Android

- Android 8.0 or later; the current native library includes arm64 and x86-64.
- Uses the existing `core/mobile` package through a generated AAR, not a renamed desktop executable.
- Android's VPN permission and a persistent foreground notification manage the device tunnel.
- App rules are direct-app exclusions. The app excludes itself from its VPN to prevent routing loops.
- Private application storage, disabled account backup/device transfer, authenticated loopback API and no account bundled in the APK.
- A positive connection label requires a fresh successful test for the current connection; a previous test is not reused after reconnect or network change.

Build dependencies: JDK 17, Android SDK/platform 36, build-tools 35.0.0, NDK 27.2.12479018, Go matching `core/go.mod`, and the generated Gradle 8.11.1 wrapper.

```powershell
$env:ANDROID_HOME = '<Android SDK>'
$env:ANDROID_NDK_HOME = '<NDK 27.2.12479018>'
# Generate app/libs/core.aar from core/mobile with gomobile first.
.\mobile\android\gradlew.bat -p mobile\android :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

The manual **Mobile builds** GitHub Actions workflow performs the complete binding and app build. It produces a debug-signed test artifact and a separate unsigned release artifact. The published APK is signed privately with the preserved III85III release key. Private keys never enter the repository, CI artifacts or application assets.

## iOS

The app and Packet Tunnel extension are in `ios/`. The separate `apple-engine` module adapts Apple's public `packetFlow` interface to an in-memory TCP/DNS stack and the existing core's proxy. It does not use a private utun file descriptor or change the account protocol.

- iOS 15 or later, SwiftUI interface and a Network Extension.
- The foreground app runs the engine while managing accounts. Before connecting, it stops that instance; the extension then owns the shared profile. While connected, API messages stay inside the app/extension relationship and never expose the loopback token.
- A normal personal iOS VPN does not provide a generic picker for routing arbitrary installed apps individually. The interface explains this rather than showing a nonfunctional selection list.
- TCP and local DNS are supported by the adapter. Arbitrary UDP and enforced Telegram-bot-only routing are not implemented.

On macOS, install Xcode, Go and XcodeGen. Generate `ios/Frameworks/III85IIICore.xcframework` with gomobile from `apple-engine`, then run `xcodegen generate` in `ios/`. The **Mobile builds** workflow builds both simulator and device applications without signing and produces an explicitly unsigned IPA.

An unsigned IPA cannot be installed as an ordinary App Store/TestFlight application. A later signed build needs an Apple development team, matching app and extension provisioning, the Network Extension capability and the App Group `group.io.github.iii85iii.vpn`.

## Validation limits

Android 2.3.2 and unsigned iOS 2.2.0 have separate build and validation results. See [validation details](VALIDATION.md) for the exact checks and remaining device/store work.

Android has three main tabs, saved-account selection and direct-app rules in Settings. In-app updates use public GitHub metadata, verified downloads and Android's user-confirmed PackageInstaller. This direct GitHub build requests permission to install updates; a future Google Play build must use Play's update mechanism instead.

The Apple packet adapter has synthetic packet/DNS/TCP relay tests. The Android application has connection-state unit tests and Android Lint. Successful compilation and these tests do not establish real Mozilla sign-in, quota display, device routing, network handover or background survival. Real-device tests remain required before store publication. No verified kill-switch or universal sign-in fix is claimed.

References: [Android VPN development](https://developer.android.com/develop/connectivity/vpn), [Apple Packet Tunnel Provider](https://developer.apple.com/documentation/networkextension/nepackettunnelprovider), [Go mobile bindings](https://pkg.go.dev/golang.org/x/mobile/cmd/gomobile).

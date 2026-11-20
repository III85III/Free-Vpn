# Android build

The app uses Java 17, Android SDK platform 34, and the Go mobile library in
`app/libs/core.aar`. From this directory, run:

```powershell
./gradlew.bat :app:testDebugUnitTest :app:lintRelease :app:assembleRelease
```

The matching Go source is in `../../core`. To rebuild the Android library,
install the Go mobile tools and Android NDK, then run from the `core` folder:

```powershell
gomobile bind -target=android -androidapi 26 -o ../mobile/android/app/libs/core.aar ./mobile
```

Release signing credentials are intentionally kept outside the source. Use the
existing project signing key when preparing an update for installed copies.

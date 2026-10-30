# Changelog

## Windows 2.2.3 / Android 2.3.3

- Simplify Home to the main connection control, server selection and account/traffic status. Move supporting options into Help and About.
- Show a one-time account reminder on an empty first installation. It only has Got it; sign-in and verification remain unchanged.
- Download updates inside the app. Windows replaces the existing installation and reopens without repeated setup questions; Android moves directly from verification to its required system installation confirmation.

- Make detected newer app releases mandatory: Update or Exit, with no Later action.
- Block VPN use while a required update is pending and remember the requirement across reopening.
- Keep retry available after a failed check, download or installation. User confirmation from the operating system is still required.
- Check while the app is open approximately every five minutes, and shortly after opening.
- Preserve local accounts and keep the backend unchanged. Previously distributed clients need one normal upgrade before this policy applies to them.

## Windows 2.2.2 / Android 2.3.2

- Fix Windows Whole computer startup when the service cannot read the installed engine. New installations grant SYSTEM read/execute access; the app also repairs this program-folder permission before starting TUN. Account storage is separate.
- Show connection-button feedback immediately, refresh transitions sooner and remove a fixed delay from server switching.
- Keep Recommended, Fastest and automatic server choices above countries, including during ping sorting and search.
- Use shorter labels such as Test ping and remove repeated copy from the main screens.
- Show Windows errors and confirmations in themed app dialogs with optional technical details. Android errors use a short summary with expandable details.
- Keep the existing backend, Android signing identity and in-app update path.

## Windows 2.2.1 / Android 2.3.1

- Add platform-specific GitHub update checks, update prompts, verified downloads and user-confirmed installation from the app. No personal VPS is required.
- Correct the Windows saved-account preflight and keep connection/tunnel errors visible during polling.
- Reconcile verification HTTP 409 with the current sign-in state and prevent duplicate answers to a stale prompt.
- Add explicit account selection, Windows VPN-service repair and a limited connection-status export.
- Simplify Android navigation to VPN, Locations and Settings, with consistent dialogs and account/direct-app settings.
- Validate native TUN synthetic DNS and actual in-app PackageInstaller updates on Android 8, 14 and 16. Real Mozilla egress and Windows routing remain separate device checks.

## Frontend reliability

- Fetch diagnostic logs only while Help is open and refresh them independently of connection status.
- A delayed or failed log request no longer holds the status loop or resets a verified connection display. Log batches are appended once and stale entries are skipped.
- Fourteen loopback HTTP/UI checks cover hidden-page request counts, delayed/failed diagnostics, connection state, duplicate requests and log cursor updates.

- Add a keyboard-reachable Show/Hide password control with descriptive accessibility names and preserved text selection.
- Mask a revealed password immediately on submit, disable reveal while the request is pending and restore correction controls after failure.

- Treat closing the credentials window as cancellation even before its request returns; a late response cannot reopen or accept the closed window.
- Cancel only that temporary sign-in session when its identifier arrives late, suppress duplicate submissions and keep error/retry handling on active windows.
- Eleven loopback HTTP/window-lifecycle checks cover delayed success, delayed failure, duplicate submit, password clearing and cancellation.

- Measure server latency only on an explicit click; opening the app or Locations no longer starts a background scan.
- Fix the manual measurement button being blocked by the general action lock.
- Keep connection controls available while latency is measured, prevent duplicate scans and report measurement failures.
- Verify this with 12 real loopback HTTP/UI regression checks. No VPN throughput improvement is claimed.

## 2.2.0-preview.1

- Restore the unchanged original Firefox VPN Client engine used by the working desktop version.
- Retain III85III interface/installer branding and original engine GPL/source notices.
- Keep verification codes in a separate window after email/password entry.
- Restore the engine's actual app-exclusion semantics; remove the experimental Node/sing-box implementation from this release.
- Verify original component hashes, 44 desktop checks, 507 layout checks and disconnected engine startup.
- Real sign-in for previously affected users remains to be confirmed.

## 2.1.0-preview.1

- Independent Mozilla account client, HTTP/2 gateway and selected-process routing configuration.
- Integration with the existing III85III desktop interface and luxury 85 mark.
- Manual account profiles protected by Windows DPAPI.
- Bounded connection recovery, favorites/recent destinations and service quota display.
- Persian/English, light/dark controls, tray actions, shortcuts and safe diagnostic export.
- Loopback, vault, routing-policy and desktop geometry tests.

This is a development preview. Live Mozilla sign-in and selected-versus-direct app egress remain unverified; it is not a stable release or an independent VPN service.

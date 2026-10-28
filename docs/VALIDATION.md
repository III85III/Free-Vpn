# Validation — III85III VPN 2.2.0

The release engine is built from the owner's current Go source. Its hashes are recorded in the packaged core/manifest.json. The earlier original-engine hashes describe historical builds, not the current binaries.

The interface, engine and installer identify III85III as their publisher. Application and engine source and dependency licenses are included. Builds without an update signing key now refuse update verification and privileged TUN replacement instead of accepting empty signatures; the signing suite and three TUN signed-update tests passed after this limited correction. No private signing key is distributed.

Desktop API/state regression checks and geometry checks exercise the interface, including the email/password-only first screen and separate email/TOTP verification window. These checks do not establish successful real sign-in or remote service compatibility.

Completed: 44 desktop API/state checks and 507 geometry/verification-window checks passed. An isolated run of the actual original engine 1.0.4 passed startup, token-protected loopback API, empty-account state, app inventory and exclusion-rule persistence checks. It stayed disconnected; no real network tunnel was tested. The app binary reports III85III as product and company, version 2.2.0.0.

Twelve additional frontend latency regression checks pass against a real local HTTP fixture: the measurement button sends the request, repeated clicks do not queue extra scans, connection controls remain available, results appear in the list and errors allow a retry. These tests use synthetic data and do not exercise the owner's core or a real remote VPN connection.

Six additional latency checks cover the Help action sharing that same scan: it opens the location results page, disables both scan entry points, keeps connection controls available, resists re-enabling during polling, deduplicates cross-page requests and restores both controls after completion. The current source latency suite has 18 checks; the published 2.2.0 installer predates this source-only change.

Eleven sign-in window lifecycle checks pass against a delayed local HTTP fixture. Closing the credentials window clears the password, late replies cannot reopen or accept it, a returned temporary session is cancelled, duplicate submissions are suppressed and active errors allow retry. These are frontend lifecycle checks; they do not establish real Mozilla authentication or resolve the reported remote HTTP 406.

Ten additional credential-usability checks cover password reveal/masking, accessible input names, keyboard tab ordering, non-overlapping controls, preserved selection and masking during validation/submission. The combined credential/lifecycle suite now has 21 checks. Screen-reader operation and final display scaling still require manual review; no core authentication behavior was changed.

Eight Windows BAT/Python process-environment experiments pass using generated scripts and loopback HTTP/TLS servers only. They show both a proxy-aware request taking its configured proxy path and a raw socket bypassing those same environment settings. HTTPS checks include CONNECT with destination certificate verification and rejection without direct fallback. Temporary test certificates are generated and removed locally. The parent environment is unchanged. This validates compatibility and a limitation, not the owner's core or a completed Telegram-bots-only feature; see [bot routing status](bot-routing.md).

Fourteen polling checks use concurrent loopback HTTP fixtures: hidden diagnostics generate no log calls, status continues while a log response is held, a diagnostic HTTP failure does not change verified connection state, errors do not echo private response details, and cursor-based log batches skip already displayed entries. This measures frontend request behavior and responsiveness, not VPN throughput.

Thirty-three installer checks use generated ZIPs and disposable folders. Missing executables, mixed archive roots, traversal paths, duplicate filenames and file/directory conflicts leave the existing installation intact. The complete replacement is prepared before switching directories; an injected promotion failure verifies rollback. Successful replacement removes obsolete application files while separate fixture data remains unchanged. The installer compiles independently. These checks do not run an installed application, change the owner's core, validate a final release payload, or simulate power loss during the directory switch.

Twenty-nine keyboard checks exercise the custom navigation, location picker, routing cards and tray toggle in a disposable window. Enter/Space activate on release, held keys do not repeat actions, focus loss or disabling cancels a pending activation, modified shortcuts are ignored, and accessible names, roles and selected/checked states are exposed. Focus indicators are drawn on these controls. This does not establish complete screen-reader compatibility; manual assistive-technology and display-scaling review remain necessary. No routing or authentication request is made by these isolated control tests.

Required live checks:
1. The previously affected user signs in and completes verification.
2. A real remote connection succeeds within the account's normal entitlement and quota.
3. Verify the current engine's app exclusions and connection/disconnection behavior.

This release does not provide the experimental include-only routing, DPAPI account vault or independent Node/sing-box core. A universal resolution of HTTP 406 is not claimed.

The earlier original-core snapshot and build metadata refer to historical versions. Current engine source is included under source/core in the installer.

The rebuilt 2.2.0 engine passed an isolated empty-profile startup check with local-proxy-only configuration: authenticated loopback API, empty account list, disconnected status and clean shutdown. No real account was loaded, no TUN service was installed and no VPN connection was attempted by this check.

Twelve new shortcut checks passed for Ctrl+1 through Ctrl+6 page navigation, context-aware Ctrl+F, selecting existing search text, Escape filter clearing and F1 Help. The current native suite totals 672 checks, including 519 layout/shortcut checks. Live account success reported by the owner has not been independently reproduced for the previously affected account.

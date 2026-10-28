# Validation — restored original engine

The original engine components are checked against pinned SHA-256 digests and are identical to those in the working desktop 1.3.3 build. They have not been renamed internally, republished as newly authored binaries or rebuilt from the snapshot.

The interface and installer identify III85III as their publisher. The original engine's authorship, licenses and source snapshot remain included.

Desktop API/state regression checks and geometry checks exercise the interface, including the email/password-only first screen and separate email/TOTP verification window. These checks do not establish successful real sign-in or remote service compatibility.

Completed: 44 desktop API/state checks and 507 geometry/verification-window checks passed. An isolated run of the actual original engine 1.0.4 passed startup, token-protected loopback API, empty-account state, app inventory and exclusion-rule persistence checks. It stayed disconnected; no real network tunnel was tested. The app binary reports III85III as product and company, version 2.2.0.0.

Twelve additional frontend latency regression checks pass against a real local HTTP fixture: the measurement button sends the request, repeated clicks do not queue extra scans, connection controls remain available, results appear in the list and errors allow a retry. These tests use synthetic data and do not exercise the owner's core or a real remote VPN connection.

Eleven sign-in window lifecycle checks pass against a delayed local HTTP fixture. Closing the credentials window clears the password, late replies cannot reopen or accept it, a returned temporary session is cancelled, duplicate submissions are suppressed and active errors allow retry. These are frontend lifecycle checks; they do not establish real Mozilla authentication or resolve the reported remote HTTP 406.

Four Windows BAT/Python process-environment experiments pass using generated scripts and loopback HTTP servers only. They show both a proxy-aware request taking its configured proxy path and a raw socket bypassing those same environment settings. The parent environment is unchanged. This validates a limitation, not a completed Telegram-bots-only feature; see [bot routing status](bot-routing.md).

Required live checks:
1. The previously affected user signs in and completes verification.
2. A real remote connection succeeds within the account's normal entitlement and quota.
3. Verify the original engine's app exclusions and connection/disconnection behavior.

This release does not provide the experimental include-only routing, DPAPI account vault or independent Node/sing-box core. A universal resolution of HTTP 406 is not claimed.

The upstream core's build metadata identifies commit 88c6f0b3f2795daa09226ec3f7ecea6a220e36db with vcs.modified=true. The corresponding public snapshot is included; exact source/build reproducibility remains unverified.

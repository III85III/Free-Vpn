# Changelog

## Frontend reliability

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

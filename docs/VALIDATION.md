# Validation status — 2.1.0-preview.3

The preview integrates the independent core with the accepted III85III
desktop interface. It is a development preview, not a stable release.

The owner reports successful sign-in for their account, but another user
still encounters HTTP 406 despite successful sign-in in Firefox. The
in-app account flow is not generally validated. This update keeps
the account implementation and separates the credentials and verification
windows. The single-file EXE installer contains the full runtime, source
and required third-party licenses.

## Completed local checks

- 26 Node protocol, account, adapter, recovery and real loopback HTTP/2 tests.
- 44 desktop connection-state, authenticated API and configuration checks.
- 896 desktop geometry, translation/theme, typography, sorting, first-run
  navigation and safe-diagnostic checks across several window sizes.
- Generated selected-app configuration accepted by the actual sing-box binary.
- Disconnected smoke: private local API authentication, process inventory,
  app-choice persistence, account-required connection and real Windows
  DPAPI roundtrip. No remote VPN connection or routing adapter was started.
- Original third-party engine binaries and source archive are excluded from
  the independent source/release allowlists. Runtime dependencies keep
  their respective licenses.
- Email, profiles, session tokens, private settings, runtime logs and internal
  project notes are excluded from publication and downloadable packages.

These checks establish only the behaviors they exercise. A simulated
service, a configuration check and geometry assertions are not substitutes
for the following live tests.

## Still required before stable release

1. Manual login to an eligible Mozilla account and supported verification.
2. A real, normally issued proxy pass and service-reported quota.
3. End-to-end remote TLS through the actual proxy pass.
4. Separate measurements of selected and unselected app egress, including
   DNS behavior, IPv4/IPv6, network changes, failure recovery and shutdown.
5. Owner review of the final interface on their Windows display/scaling.

The preview does not claim full behavioral equivalence to another client,
an audited kill switch, universal Firefox-account eligibility, unlimited
quota, arbitrary-port/UDP support, or availability on other operating
systems. Store distribution is future work.

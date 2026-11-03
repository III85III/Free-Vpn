# III85III VPN for Mac

Free desktop client for macOS 13 or later. The universal app supports Apple Silicon and Intel.

Open the disk image and drag **III85III VPN** to **Applications**. Open it from Applications.
This build is ad-hoc signed and has not been notarized by Apple. macOS may block it;
review the download's origin before using the system's Privacy & Security approval.
Do not disable Gatekeeper.

Sign in with your Mozilla account in Accounts. Alternatively, use **Account > Import
private account file** to import an existing account file kept on your own Mac.
The JSON file contains either `email` and `refresh_token` fields or an `accounts` array
of these records. Treat it like a password: never put it on GitHub or share it publicly.
The app does not contain prepared accounts or promise a fresh 50 GB allowance.
Service eligibility, account availability and the actual remaining quota depend on Mozilla.

The native window has Connect, status and remaining allowance. Servers, Accounts and
Settings open the engine's authenticated local dashboard in your browser. The app
must remain open while using that dashboard; this is not a browser extension.

Press Connect in the app. System proxy mode applies to apps that honor macOS proxy
settings. Whole Mac mode requires **Connection > Install Whole Mac helper**, macOS
administrator approval, and choosing TUN in Settings. The backend carries supported TCP
traffic; this is not a verified kill switch or a guarantee that every protocol is tunneled.

Closing the last window quits the app and asks its engine to restore network settings.
Your accounts remain in `~/Library/Application Support/III85III VPN/user-data`.
There is no automatic Mac update installer in this first build. Use the official GitHub
release for updates. Windows and Android updates are unchanged.

Validation covers compilation, packaging, code-signature integrity, startup with an
empty temporary profile, native controls, authenticated local API access and the
browser dashboard's login session and page response.
Real Mozilla sign-in, quota refresh, public VPN egress and elevated TUN routing have
not yet been validated on an owner's Mac. This is an initial Mac build, not a claim
of complete platform parity. Dependency licenses are included inside the app.

Support: https://t.me/cabicy

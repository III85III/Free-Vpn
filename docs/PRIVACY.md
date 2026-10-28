# Privacy

- Each installation signs into its owner's Mozilla account. No developer account is distributed.
- Local account profiles contain session credentials encrypted with Windows DPAPI for the current Windows user. They cannot be moved as usable credentials to another Windows user simply by copying the file.
- The application does not save the account password. Runtime memory and the operating system are outside a claim of absolute secrecy.
- Local API access requires a random per-process token and binds to IPv4 loopback. No public control service is opened.
- Mozilla receives account-service requests and the service infrastructure handles tunneled traffic. DNS and application routing need the live validation described in VALIDATION.md.
- III85III runs no separate VPS, account broker or analytics service.
- Settings, application selections, cached location records and encrypted profiles stay in the current user's local application data directory. These are excluded from the public source and release archive.
- Additional accounts are selected manually. Quotas are individual service entitlements; the client does not combine or reset them or rotate accounts automatically on quota refusal.
- The safe diagnostic summary uses an allowlist of app version, connection phase, adapter state and retry count. It excludes email, account identifiers, credentials, addresses, file paths and raw logs.

Do not attach account vaults, settings, authentication responses or raw runtime logs to a public GitHub issue. Remove an account from the Account page if you no longer want its session saved locally. Service-side invalidation is controlled by Mozilla.

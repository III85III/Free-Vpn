# Contributing

Start with a focused issue or small pull request describing the concrete problem and expected behavior. Useful areas include real-network validation, accessibility, Persian translation, process routing, account-flow compatibility and reliability under network changes.

Run `node --test test/*.test.cjs` and, on Windows, `tools/test.ps1`. Use synthetic accounts and local HTTP/2 fixtures; do not put a real account or token in tests. Normal remote-service eligibility and quotas must remain enforced. Do not add automatic account rotation after quota failure.

The project currently supports only a Windows development preview. Discuss platform work before claiming Android, iOS, macOS or Linux support. Keep the interface quiet and simple, preserve the III85III branding, and retain dependency licenses. New project contributions are under the repository's MIT license.

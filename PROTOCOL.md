# Protocol references

The client implementation is authored for III85III. These references describe public protocol facts; the client does not claim Mozilla endorsement or ownership of third-party libraries.

- Mozilla Accounts one-password derivation: https://mozilla.github.io/ecosystem-platform/explanation/onepw-protocol
- Mozilla authentication schemes: https://mozilla.github.io/ecosystem-platform/reference/authentication-schemes
- Mozilla Accounts API: https://mozilla.github.io/ecosystem-platform/api
- Official Guardian client behavior: https://github.com/mozilla-firefox/firefox/blob/main/toolkit/components/ipprotection/fxa/GuardianClient.sys.mjs
- Official quota and proxy-pass types: https://github.com/mozilla-firefox/firefox/blob/main/toolkit/components/ipprotection/GuardianTypes.sys.mjs
- Official server catalog: https://firefox.settings.services.mozilla.com/v1/buckets/main/collections/vpn-serverlist/records
- Node.js HTTP/2: https://nodejs.org/api/http2.html
- sing-box routing and process rules: https://sing-box.sagernet.org/configuration/route/rule/

TLS verification is retained. The Mozilla-published account client identifier is public protocol metadata, not a developer credential. The client uses the service's ordinary authentication, entitlement and quota decisions. Browser-version-conditional location records are not evaluated as arbitrary expressions or used to impersonate a browser version.

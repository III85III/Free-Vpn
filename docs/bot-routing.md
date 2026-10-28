# Telegram bot routing: validation status

The requested BAT bot launcher is **not implemented or enabled**. Setting a child process's proxy environment is a possible compatibility aid, not enforced routing for every application started by that BAT.

## Local experiment

`tools/test-bot-proxy.py` creates its own BAT and Python fixture in `.build/`. It never opens or runs a user's BAT, reads bot settings, signs in to Telegram or contacts an external host. Python runs in isolated mode without site packages. Two loopback HTTP servers identify the route actually used.

The experiment checks four cases:

1. A proxy-aware standard-library HTTP request launched by BAT reaches the private proxy fixture.
2. A raw TCP client launched by the same BAT with the same proxy variables reaches the direct fixture instead. This is the counterexample to treating environment variables as process isolation.
3. An explicitly direct control request reaches the same local destination directly.
4. The proxy-aware client reports a proxy error without silently falling back to the direct fixture.

The parent process's proxy environment must remain unchanged in every case. No system proxy, route, adapter or core configuration is written.

These are local HTTP/process-environment experiments. They do **not** validate HTTPS CONNECT, DNS, IPv6, arbitrary descendants, any real Telegram library or actual VPN egress. They do not establish the desired "all bot traffic proxied, everything else direct" guarantee.

## Requirements before enabling the feature

- Determine the selected script's actual runtime/client and configure its supported proxy mechanism explicitly. Unknown or proxy-ignoring clients must not be presented as isolated.
- Confirm the owner's ready core supports the needed local proxy mode and verify system/TUN routing is off before launching. Do not invert the existing app-exclusion list and call it include-only routing.
- Validate selected and control traffic separately, including errors and child processes. Test with synthetic fixtures first; a user's BAT is executed only through their own explicit launch action.
- Do not read or log bot tokens, account sessions, private configuration or the BAT's full contents for diagnostics.

Python's [ProxyHandler documentation](https://docs.python.org/3.12/library/urllib.request.html#urllib.request.ProxyHandler) describes environment-based proxy discovery. [aiohttp proxy support](https://docs.aiohttp.org/en/stable/client_advanced.html#proxy-support) requires explicit configuration or `trust_env=True`; environment discovery is not enabled by default. [Telethon's proxy instructions](https://docs.telethon.dev/en/stable/basic/signing-in.html#signing-in-behind-a-proxy) use an explicit client proxy parameter. These clients cannot be assumed to inherit identical routing from a BAT environment alone.

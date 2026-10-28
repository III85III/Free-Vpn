# Privacy

Each user signs in to their own Mozilla account. No developer account, email address, saved password, session token, local settings or runtime log is included in downloads or this repository.

The original Firefox VPN Client engine performs account authentication and storage. The III85III launcher restricts its data directory to the current Windows user and SYSTEM. The upstream source supports saving an encrypted password for reauthentication with an encryption key stored locally beside the credentials. This version does not use the experimental DPAPI vault. Protect the whole data directory; do not upload it or share its encryption key.

The app uses the original engine's authenticated loopback API. Mozilla and its service infrastructure receive the requests needed for authentication and traffic forwarding. The III85III interface adds no analytics server or private VPS.

The original desktop account directory is kept separate from installed program files. Replacing the interface does not publish or copy those accounts into the installer. Do not post raw logs, account folders or screenshots containing private information in public issues.

[Original engine source](https://github.com/armanlife/firefox-vpn-client) · [Licenses](../licenses/NOTICE.txt)

<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · **🇩🇪 Deutsch** · [🇫🇷 Français](README.fr.md) · [🇪🇸 Español](README.es.md) · [🇧🇷 Português](README.pt.md) · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · [🇯🇵 日本語](README.ja.md) · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**Kostenloser Windows-Client für dein eigenes Mozilla-Konto**

III85III VPN ist ein lokaler Client für berechtigte Mozilla-Konten. Ein eigener VPS ist nicht nötig; der Datenverkehr nutzt die entfernte Infrastruktur des Mozilla-Dienstes.

> **Entwicklungsvorschau** — Die echte Mozilla-Anmeldung und der Ausgang ausgewählter Apps sind noch nicht praktisch bestätigt. Dies ist keine stabile Version. Kostenlose Software bedeutet weder unbegrenztes Datenvolumen noch einen kostenlosen Dienstanspruch. Mozilla bestimmt Berechtigung, Regionen und Kontingente.

## Funktionen

- Nur ausgewählte Apps nutzen den VPN-Ausgang; andere Apps verbinden sich direkt.
- Mehrere manuell auswählbare Konten mit lokal durch Windows DPAPI geschützten Sitzungen und getrennten Kontingenten.
- Favoriten, letzte Standorte, Latenzsortierung und verzögerte Wiederverbindung; manuelles Trennen und Konto-/Kontingentfehler stoppen Versuche.
- Infobereich-Menü, persische/englische Oberfläche, helles/dunkles Design und bereinigte Diagnoseberichte.

## Download und Einstieg

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. Lade das Windows-x64-ZIP unter Releases herunter und entpacke den gesamten Ordner.
2. Starte III85III VPN.exe; die App startet getrennt.
3. Füge dein eigenes Mozilla-Konto hinzu und bestätige die Anmeldung selbst. Gib dein Google-Passwort nicht in die App ein.
4. Wähle Apps aus, speichere sie, wähle einen Standort und verbinde dich. Prüfe gegebenenfalls die Windows-Routingfreigabe.

## Grenzen und Datenschutz

Unterstützt wird Web-TCP auf Port 80, 443, 8080 und 8443; UDP ausgewählter Apps wird abgelehnt. DNS, IPv6, tatsächlicher App-Ausgang und Ausfallverhalten benötigen Live-Tests. Konto, E-Mail und Sitzungen des Entwicklers sind nicht enthalten. Passwörter werden nicht im Kontospeicher abgelegt.

## Prüfungen und Ausblick

25 Node-Tests, 44 Desktop-Kernprüfungen und 891 Layoutprüfungen bestanden. Sie ersetzen keinen echten Verbindungstest. Android, iOS, macOS, Linux und App-Stores sind Zukunftspläne.

[Vollständige Anleitung und Quellcode-Build](../../README.md#build-from-source) · [Privacy](../../PRIVACY.md) · [Validation](../../VALIDATION.md) · [Roadmap](../../ROADMAP.md) · [Protocol](../../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


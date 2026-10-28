<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · [🇩🇪 Deutsch](README.de.md) · **🇫🇷 Français** · [🇪🇸 Español](README.es.md) · [🇧🇷 Português](README.pt.md) · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · [🇯🇵 日本語](README.ja.md) · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**Un client Windows gratuit pour votre propre compte Mozilla**

III85III VPN est un client local pour les comptes Mozilla éligibles. Aucun VPS personnel n’est nécessaire : le trafic utilise l’infrastructure distante du service Mozilla.

> **Aperçu de développement** — La connexion réelle à Mozilla et la sortie des applications sélectionnées restent à valider en conditions réelles. Ce n’est pas une version stable. La gratuité du logiciel ne garantit ni un quota illimité ni un droit gratuit au service ; Mozilla définit l’éligibilité, les régions et les quotas.

## Fonctionnalités

- Seules les applications sélectionnées passent par le VPN ; les autres utilisent une connexion directe.
- Plusieurs comptes choisis manuellement, sessions locales protégées par Windows DPAPI et quotas séparés.
- Favoris, destinations récentes, classement par latence et reconnexion progressive arrêtée après déconnexion manuelle ou refus de compte/quota.
- Menu de notification, interface persane/anglaise, thèmes clair/sombre et diagnostic sans identifiants de compte.

## Téléchargement et démarrage

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. Téléchargez III85III-VPN-Setup.exe dans Releases et lancez-le ; les composants nécessaires sont extraits automatiquement.
2. Lancez III85III VPN.exe ; l’application démarre déconnectée.
3. Ajoutez votre propre compte Mozilla et effectuez vous-même la vérification. Ne saisissez pas votre mot de passe Google dans l’application.
4. Sélectionnez et enregistrez les applications, choisissez une destination puis connectez-vous. Vérifiez l’autorisation Windows si elle apparaît.

## Limites et confidentialité

Seul le TCP web sur les ports 80, 443, 8080 et 8443 est pris en charge ; l’UDP des applications sélectionnées est refusé. DNS, IPv6, sorties réelles et incidents nécessitent encore des essais réels. Aucun compte, e-mail ou session du développeur n’est distribué. Les mots de passe ne sont pas enregistrés dans le coffre de comptes.

## Validation et avenir

25 tests Node, 44 contrôles du cœur de bureau et 891 contrôles de mise en page ont réussi. Ils ne remplacent pas un essai de connexion réel. Android, iOS, macOS, Linux et les boutiques sont des projets futurs.

[Guide complet et compilation](../OVERVIEW.md#build-from-source) · [Privacy](../PRIVACY.md) · [Validation](../VALIDATION.md) · [Roadmap](../ROADMAP.md) · [Protocol](../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


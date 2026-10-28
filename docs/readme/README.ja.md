<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · [🇩🇪 Deutsch](README.de.md) · [🇫🇷 Français](README.fr.md) · [🇪🇸 Español](README.es.md) · [🇧🇷 Português](README.pt.md) · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · **🇯🇵 日本語** · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**自分の Mozilla アカウントで使う無料の Windows クライアント**

III85III VPN は、サービスの利用条件を満たす Mozilla アカウント向けのローカルクライアントです。個人の VPS は不要で、Mozilla サービスのリモート設備を利用します。

> **開発プレビュー** — 実際の Mozilla ログインと選択アプリの通信出口は、まだ実環境での検証が必要です。安定版ではありません。アプリが無料でも、無制限の通信量や無料のサービス利用資格は保証されません。資格、地域、上限は Mozilla が決定します。

## 機能

- 選択したアプリだけが VPN 出口を使い、他のアプリは直接接続します。
- 複数アカウントの手動切り替え、Windows DPAPI で保護するローカルセッション、アカウント別の利用枠。
- お気に入り、最近の接続先、遅延順の表示、間隔を空けた再接続。手動切断やアカウント・上限エラーで再試行を停止します。
- トレイメニュー、ペルシャ語・英語 UI、ライト・ダークテーマ、アカウント識別情報を含まない診断概要。

## ダウンロードと開始

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. Releases から III85III-VPN-Setup.exe を取得して実行します。必要なファイルは自動展開されます。
2. III85III VPN.exe を実行します。起動時は未接続です。
3. 自分の Mozilla アカウントを追加し、認証は自分で行ってください。Google のパスワードをアプリに入力しないでください。
4. アプリを選択して保存し、接続先を選んで接続します。Windows のルーティング許可が表示されたら確認してください。

## 制限とプライバシー

対応するのはポート 80、443、8080、8443 の Web TCP のみです。選択アプリの UDP は拒否します。DNS、IPv6、実際の出口、障害時の挙動は実測待ちです。開発者のアカウント、メール、セッションは配布されず、パスワードはアカウント保管庫に保存されません。

## 検証と今後

Node テスト 25 件、デスクトップのコア確認 44 件、レイアウト確認 891 件に合格しました。実接続試験の代わりにはなりません。Android、iOS、macOS、Linux、ストア公開は将来の計画です。

[詳細ガイドとソースからのビルド](../OVERVIEW.md#build-from-source) · [Privacy](../../PRIVACY.md) · [Validation](../../VALIDATION.md) · [Roadmap](../../ROADMAP.md) · [Protocol](../../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


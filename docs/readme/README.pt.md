<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · [🇩🇪 Deutsch](README.de.md) · [🇫🇷 Français](README.fr.md) · [🇪🇸 Español](README.es.md) · **🇧🇷 Português** · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · [🇯🇵 日本語](README.ja.md) · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**Cliente gratuito de Windows para sua própria conta Mozilla**

III85III VPN é um cliente local para contas Mozilla elegíveis. Você não precisa de VPS próprio: o tráfego utiliza a infraestrutura remota do serviço Mozilla.

> **Prévia de desenvolvimento** — O login real e a saída dos aplicativos selecionados ainda precisam de validação ao vivo. Esta não é uma versão estável. Aplicativo gratuito não significa dados ilimitados nem acesso gratuito garantido ao serviço; a Mozilla define elegibilidade, regiões e cotas.

## Recursos

- Somente aplicativos selecionados usam a saída VPN; os demais se conectam diretamente.
- Várias contas com seleção manual, sessões locais protegidas pelo Windows DPAPI e cotas separadas.
- Favoritos, destinos recentes, ordenação por latência e reconexão gradual que para após desconexão manual ou recusa de conta/cota.
- Menu na bandeja, interface persa/inglesa, temas claro/escuro e relatório de diagnóstico sem identificadores de conta.

## Download e início

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. Baixe III85III-VPN-Setup.exe em Releases e execute; os componentes necessários são extraídos automaticamente.
2. Abra III85III VPN.exe; o aplicativo inicia desconectado.
3. Adicione sua própria conta Mozilla e conclua a verificação pessoalmente. Não digite sua senha Google no aplicativo.
4. Selecione e salve os aplicativos, escolha um destino e conecte. Revise a permissão de roteamento do Windows, se solicitada.

## Limites e privacidade

Apenas TCP web nas portas 80, 443, 8080 e 8443 é suportado; UDP dos aplicativos selecionados é rejeitado. DNS, IPv6, saída real e comportamento em falhas ainda exigem testes ao vivo. Conta, e-mail e sessões do desenvolvedor não são distribuídos. Senhas não são gravadas no cofre de contas.

## Validação e futuro

Passaram 25 testes Node, 44 verificações do núcleo e 891 de layout. Eles não substituem testes de conexão real. Android, iOS, macOS, Linux e lojas são planos futuros.

[Guia completo e compilação](../OVERVIEW.md#build-from-source) · [Privacy](../../PRIVACY.md) · [Validation](../../VALIDATION.md) · [Roadmap](../../ROADMAP.md) · [Protocol](../../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


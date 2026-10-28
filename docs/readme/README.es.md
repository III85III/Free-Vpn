<!-- language-selector:start -->
[🇬🇧 English](../../README.md) · [🇮🇷 فارسی](README.fa.md) · [🇸🇦 العربية](README.ar.md) · [🇹🇷 Türkçe](README.tr.md) · [🇩🇪 Deutsch](README.de.md) · [🇫🇷 Français](README.fr.md) · **🇪🇸 Español** · [🇧🇷 Português](README.pt.md) · [🇷🇺 Русский](README.ru.md) · [🇨🇳 简体中文](README.zh.md) · [🇯🇵 日本語](README.ja.md) · [🇮🇳 हिन्दी](README.hi.md)
<!-- language-selector:end -->

<p align="center"><img src="../../native/assets/brand/iii85iii-luxury.png" width="112" alt="III85III"></p>

# III85III VPN

**Cliente gratuito de Windows para tu propia cuenta Mozilla**

III85III VPN es un cliente local para cuentas Mozilla elegibles. No necesitas un VPS propio: el tráfico utiliza la infraestructura remota del servicio Mozilla.

> **Vista previa de desarrollo** — El inicio de sesión real y la salida de las aplicaciones seleccionadas aún necesitan pruebas en vivo. No es una versión estable. Que la aplicación sea gratuita no implica datos ilimitados ni acceso gratuito garantizado al servicio; Mozilla decide los requisitos, regiones y cuotas.

## Funciones

- Solo las aplicaciones seleccionadas usan la salida VPN; las demás se conectan directamente.
- Varias cuentas con selección manual, sesiones locales protegidas por Windows DPAPI y cuotas independientes.
- Favoritos, destinos recientes, orden por latencia y reconexión gradual que se detiene tras desconexión manual o rechazo de cuenta/cuota.
- Menú de bandeja, interfaz persa/inglesa, temas claro/oscuro e informe de diagnóstico sin identificadores de cuenta.

## Descarga e inicio

[⬇ Windows x64 · Releases](https://github.com/III85III/III85III-VPN/releases)

1. Descarga III85III-VPN-Setup.exe desde Releases y ejecútalo; extrae automáticamente los componentes necesarios.
2. Abre III85III VPN.exe; se inicia desconectado.
3. Añade tu propia cuenta Mozilla y completa tú mismo la verificación. No introduzcas tu contraseña de Google en la aplicación.
4. Selecciona y guarda las aplicaciones, elige destino y conecta. Revisa el permiso de enrutamiento de Windows si aparece.

## Límites y privacidad

Solo se admite TCP web en los puertos 80, 443, 8080 y 8443; se rechaza UDP de las aplicaciones seleccionadas. DNS, IPv6, salida real y fallos siguen pendientes de pruebas en vivo. No se distribuyen cuentas, correo ni sesiones del desarrollador. Las contraseñas no se guardan en el almacén de cuentas.

## Validación y futuro

Pasaron 25 pruebas Node, 44 comprobaciones del núcleo y 891 de diseño. No sustituyen pruebas de conexión real. Android, iOS, macOS, Linux y las tiendas son planes futuros.

[Guía completa y compilación](../OVERVIEW.md#build-from-source) · [Privacy](../../PRIVACY.md) · [Validation](../../VALIDATION.md) · [Roadmap](../../ROADMAP.md) · [Protocol](../../PROTOCOL.md)

MIT · [LICENSE](../../LICENSE) · [Third-party licenses](../../THIRD-PARTY-NOTICES.txt)


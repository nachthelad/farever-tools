# Farever Translate 0.1.0-beta.1

Primera beta pública del traductor manual de chat para Farever.

## Antes de instalar

- Proyecto comunitario no afiliado a Shiro Games.
- Uso personal y bajo responsabilidad del usuario.
- Requiere Windows x64 y
  [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0/runtime).
- Funciona sin minimapa.
- Si Farever Minimap & DPS v1.2.3 o v1.2.4 ya está instalado, conserva y
  encadena ambos componentes.
- Un `dinput8.dll` desconocido se rechaza sin modificarlo.
- Farever debe estar cerrado.

## Instalar

1. Descargá el ZIP y `SHA256SUMS.txt`.
2. Verificá el checksum y extraé el ZIP en una carpeta aparte.
3. Ejecutá `Instalar.cmd`.

La beta soporta únicamente el build de Farever indicado en el README. Los
hashes desconocidos se rechazan sin modificar el juego.

## Funciones

- Detección del chat en solo lectura.
- Traducción manual al español mediante una API key propia de Google Cloud.
- Panel externo compacto con `T` para traducir y `×` para descartar.
- `F6` muestra u oculta el panel.
- `Alt` sola habilita o deshabilita la interacción sin cortar la cámara.
- Instalación, actualización y desinstalación con hashes y rollback.

No publiques API keys, conversaciones ni cachés en los Issues.

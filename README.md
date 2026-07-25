# Farever Translate

Traductor de chat local para Farever. El proyecto conserva el minimapa/DPS
existente, lee el chat con acceso de solo lectura y muestra traducciones al
español bajo demanda.

El paquete tiene dos componentes:

- `dinput8.dll`: proxy nativo mínimo que encadena el mod instalado como
  `farever-minimap.dll` y arranca el host.
- `FareverTranslate.Host.exe`: lector HashLink externo, panel WPF y cliente de
  Google Cloud Translation.

El proyecto no escribe memoria del juego, no automatiza entradas, no traduce
mensajes sin un clic explícito y no redistribuye el DLL del minimapa.

La configuración y los datos protegidos se guardan en
`%LOCALAPPDATA%\FareverTranslate`.

## Estado

MVP implementado para Farever x64, build de `hlboot.dat`
`7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0`.

## Compilar

Requisitos:

- .NET 8 SDK y Desktop Runtime.
- Visual Studio Build Tools 2022 con C++ x64.

Desde PowerShell:

```powershell
.\scripts\Build-Release.ps1
```

El paquete queda en `artifacts\package`.

## Instalar

Farever debe estar cerrado y el minimapa oficial compatible ya debe estar
instalado como `dinput8.dll`.

```powershell
.\artifacts\package\Install-FareverTranslate.ps1 `
  -GameDirectory 'E:\SteamLibrary\steamapps\common\Farever'
```

El script verifica el hash conocido del minimapa, crea un respaldo, lo mueve a
`farever-minimap.dll` e instala el proxy y el host. No descarga ni distribuye
el DLL oficial.

Para desinstalar:

```powershell
.\artifacts\package\Uninstall-FareverTranslate.ps1 `
  -GameDirectory 'E:\SteamLibrary\steamapps\common\Farever'
```

La desinstalación aborta si algún archivo fue modificado y solo restaura el
original cuando todos los hashes coinciden.

## Usar

- Abrir Farever en ventana o ventana sin bordes.
- Pulsar **Clave** y guardar una API key restringida a Cloud Translation API.
- Pulsar **Traducir** en una línea; no hay traducción automática.
- El panel aparece únicamente dentro del mundo y mientras Farever está en
  primer plano. Se oculta en cargas, minimizado y al cambiar de aplicación.
- Si el historial deja de producir mensajes, el lector refresca sus
  referencias y vuelve a buscar el chat automáticamente con espera progresiva.
- Usar **Calibrar** para arrastrar o redimensionar el panel junto al chat. El
  tamaño y la posición quedan guardados.
- Alternar el panel con `F6` mientras Farever está en primer plano.

Consulta [Google Cloud](docs/GOOGLE_CLOUD.md), los
[límites de seguridad](docs/SECURITY.md) y el
[diseño base](docs/superpowers/specs/2026-07-25-farever-chat-translator-design.md),
además del
[diseño del panel compacto](docs/superpowers/specs/2026-07-25-game-bound-compact-overlay-design.md).

## Pruebas

```powershell
.\scripts\Test-All.ps1
```

La suite cubre objetos y registros virtuales HashLink sintéticos (Unicode,
duplicados, punteros inválidos y reconexión), build desconocido, respuestas de
Google, DPAPI, caché, DPI, los cinco exports y el ciclo
instalación/restauración.

Quedan como pruebas manuales previas al uso cotidiano: validar el minimapa/DPS
en el juego, comprobar el ajuste visual del panel y completar una sesión de
una hora con reconexiones y cambios de zona.

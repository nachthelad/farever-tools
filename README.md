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

MVP en desarrollo para Farever x64, build de `hlboot.dat`
`7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0`.

Consulta el diseño aprobado en
`docs/superpowers/specs/2026-07-25-farever-chat-translator-design.md`.


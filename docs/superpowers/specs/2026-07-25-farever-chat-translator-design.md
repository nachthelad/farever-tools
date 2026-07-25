# Farever Chat Translator — diseño del MVP

Fecha: 2026-07-25

## Objetivo

Crear un paquete local para Windows x64 que conviva con Farever Minimap/DPS,
lea mensajes de otros jugadores con acceso de solo lectura y permita traducir
una línea seleccionada al español mediante Google Cloud Translation.

## Arquitectura

### Proxy DirectInput

Nuestro `dinput8.dll` exporta las cinco funciones utilizadas por DirectInput.
La primera llamada exportada inicializa el proxy fuera de `DllMain`, carga
`farever-minimap.dll` mediante una ruta absoluta y reenvía las llamadas. Si el
mod renombrado no está disponible, usa el `dinput8.dll` de System32.

El proxy arranca una única instancia de `FareverTranslate.Host.exe` para el PID
del juego. No lee memoria, no dibuja, no hace red y no contiene credenciales.

### Host externo

El host .NET 8 x64 abre Farever exclusivamente con
`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`. Verifica el hash de
`hlboot.dat`, resuelve metadatos HashLink por nombres y localiza el jugador
local y su historial de chat sin direcciones absolutas.

Los datos se normalizan como:

```text
ChatMessageSnapshot {
  identity,
  sender,
  channel,
  text,
  isSelf,
  isSystem
}
```

El lector descarta muestras inválidas, vuelve a localizar objetos después de
GC, reconexiones o cambios de zona y nunca escribe en el proceso.

### Panel

Una ventana WPF transparente y sin activación sigue el rectángulo cliente de
Farever. Muestra las seis líneas recientes de otros jugadores, se puede
calibrar arrastrándola y se alterna con `Ctrl+Shift+T`.

Cada fila contiene el original y un botón `Traducir`. Solo el clic explícito
envía esa línea a Google. La respuesta aparece debajo sin cambiar el foco del
juego.

### Traducción y almacenamiento

Cloud Translation Basic v2 recibe texto plano, idioma de origen automático y
destino `es`. La API key y un caché limitado se cifran con DPAPI para el
usuario actual. Los logs nunca contienen claves ni contenido del chat.

El cliente usa un timeout de ocho segundos y un único reintento para errores
de red o respuestas 5xx. Los errores de autenticación, permisos y cuota se
muestran de forma explícita.

## Instalación

La instalación exige que Farever esté cerrado. Verifica y respalda el
`dinput8.dll` existente, lo renombra a `farever-minimap.dll`, instala nuestro
proxy y el host, y registra hashes en un manifiesto.

La desinstalación verifica nuestros archivos antes de eliminarlos y restaura
el DLL original. Ante cualquier diferencia inesperada, aborta sin sobrescribir.

## Compatibilidad y límites

- Build inicial de `hlboot.dat`:
  `7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0`.
- Windows x64, ventana o ventana sin bordes.
- Destino español fijo y traducción manual.
- Sin escritura en el chat, traducción automática, fullscreen exclusivo,
  distribución pública ni parcheo del DLL oficial.
- Un build desconocido falla cerrado y muestra un diagnóstico.

## Aceptación

- Los cinco exports de DirectInput se reenvían con y sin minimapa.
- Minimap/DPS continúa funcionando.
- Un mensaje nuevo aparece en el panel en menos de un segundo.
- La traducción no roba el foco.
- Errores de API y punteros inválidos no cierran Farever.
- Instalación y desinstalación restauran byte por byte el DLL original.
- Una prueba de una hora con cambios de zona no produce crashes.

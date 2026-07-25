# F6 y recuperación automática del chat — diseño

Fecha: 2026-07-25

## Objetivo

Reemplazar `Ctrl+Shift+T` por `F6` para mostrar u ocultar el panel y recuperar
automáticamente el lector cuando Farever recrea el personaje, `chatClient` o
el historial sin producir snapshots.

## Toggle F6

- `F6` será el único toggle del panel.
- La tecla se observará únicamente cuando Farever esté en primer plano.
- Se detectará el flanco de pulsación: mantener F6 apretado no alternará el
  panel repetidamente.
- No se registrará F6 como hotkey global de Windows. De esta forma, la tecla
  seguirá disponible normalmente en otras aplicaciones.
- Los textos de ayuda del panel y el README mostrarán `F6`.

## Watchdog del lector

El sondeo normal continuará a 10 Hz. El watchdog solo actuará cuando no se
obtengan mensajes normalizados:

1. Tras cinco segundos sin snapshots, releerá desde el personaje conocido el
   `chatClient` actual y su `history`.
2. Si después de quince segundos continúa vacío, invalidará la cadena conocida
   y hará una relocalización completa del personaje local y el chat.
3. Si sigue sin encontrar mensajes, las relocalizaciones completas usarán
   esperas crecientes de 15, 30 y 60 segundos, con 60 segundos como máximo.
4. La llegada de cualquier snapshot válido reiniciará todos los contadores.
5. Una referencia inválida o una reconexión confirmada podrá activar la
   recuperación inmediatamente, pero una búsqueda completa nunca se ejecutará
   a 10 Hz.

Un historial realmente vacío seguirá siendo un estado válido. El panel podrá
permanecer visible con “Esperando mensajes de otros jugadores…”, mientras el
watchdog comprueba periódicamente que continúa conectado al historial actual.

## Seguridad y registros

- Se conserva exclusivamente `PROCESS_VM_READ` y
  `PROCESS_QUERY_LIMITED_INFORMATION`.
- No se escribirá memoria, no se inyectará código y no se simularán teclas.
- Los logs registrarán únicamente el tipo de recuperación y su resultado.
  Nunca incluirán contenido del chat, nombres de jugadores ni credenciales.

## Pruebas

- F6 alterna una sola vez por pulsación con Farever al frente.
- F6 no altera el panel cuando otra aplicación está al frente.
- Los textos ya no mencionan `Ctrl+Shift+T`.
- El refresh liviano ocurre a los cinco segundos sin snapshots.
- La relocalización completa comienza a los quince segundos y respeta el
  backoff hasta 60 segundos.
- Un mensaje válido reinicia el watchdog.
- Reconexiones y cambios de `chatClient` siguen recuperándose.
- La suite existente continúa en 7/7 y la lectura real no registra mensajes.

## Aceptación

El cambio queda aprobado cuando F6 controla el panel sin interferir fuera de
Farever y el lector vuelve a buscar el chat periódicamente sin provocar
escaneos continuos ni traducir líneas automáticamente.

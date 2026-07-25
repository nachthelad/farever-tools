# Panel compacto ligado a Farever — diseño

Fecha: 2026-07-25

## Problemas observados

La primera versión crea una ventana de `430 × 520` píxeles, sin
redimensionamiento, que puede aparecer en `Alt+Tab` y permanecer visible fuera
de Farever. También se muestra antes de que exista un personaje local.

En el build soportado el lector termina con `Metadatos de chat incompletos`.
Aunque el chat nativo recibe mensajes, el panel no obtiene snapshots y por eso
no presenta filas ni botones `Traducir`.

## Ventana y ciclo de visibilidad

El panel será una ventana auxiliar propiedad de la ventana principal de
Farever. Mantendrá `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` y
`ShowInTaskbar=false`, y notificará al shell después de aplicar los estilos.
Así no tendrá una entrada propia en `Alt+Tab`.

Su visibilidad normal requerirá simultáneamente:

- Preferencia `PanelVisible` activada.
- Ventana de Farever encontrada, restaurada y en primer plano.
- Lector HashLink en estado `Ready`.
- Personaje local, `chatClient` e historial de chat válidos.

Se ocultará durante el inicio, selección de personaje, cargas, desconexiones,
minimizado y cuando otra aplicación tenga el foco. El modo de calibración será
la única excepción: podrá activar temporalmente el panel para moverlo o
redimensionarlo.

## Tamaño y calibración

- Tamaño inicial: `360 × 280` píxeles independientes de dispositivo.
- Tamaño mínimo: `320 × 220`.
- Tamaño máximo: `620 × 720`.
- Redimensionamiento desde bordes y esquinas.
- Posición, ancho y alto persistidos en `settings.json`.
- El contenido de mensajes tendrá scroll vertical para que el panel no crezca
  por sí solo.
- Al terminar `Calibrar`, volverá al modo sin activación y ligado al juego.

## Resolución del chat

El lector conservará acceso exclusivamente de solo lectura. El build seguirá
validándose por el SHA-256 de `hlboot.dat`.

La resolución se dividirá en etapas observables:

1. Resolver `st.Player` y registrar únicamente nombres, tipos y offsets de sus
   campos.
2. Resolver `st.player.ChatClient` y registrar de la misma forma sus campos.
3. Validar los campos `isMe`, `chatClient` e `history`.
4. Localizar una única instancia local mediante `isMe`.
5. Seguir el `chatClient` actual y normalizar las entradas de `history`.

Si una etapa falla, el log indicará exactamente el tipo o campo faltante sin
registrar direcciones, nombres de jugadores ni contenido del chat. La búsqueda
de metadatos se hará de forma agrupada y acotada para no mantener un núcleo de
CPU ocupado durante minutos.

El estado `Ready` solo se publicará cuando la cadena completa sea válida. En
reconexiones, cambios de zona o GC, el lector ocultará el panel, relocalizará
los objetos y volverá a mostrarlo cuando estén listos.

## Filas y traducción

Cada snapshot aceptado de otro jugador producirá una fila con:

- Autor y canal.
- Texto original.
- Botón visible `Traducir`.
- Traducción debajo de la línea.
- Estado de red, credencial, cuota o servicio cuando corresponda.

Se conservarán como máximo seis mensajes y nunca se traducirá automáticamente.
Los mensajes propios, del sistema, duplicados o inválidos seguirán filtrados.

## Pruebas

- Comprobar que el panel no aparece en `Alt+Tab` ni en la barra de tareas.
- Alternar el foco entre Farever y otra aplicación.
- Verificar ocultamiento en selección de personaje, carga, minimizado y
  desconexión.
- Probar entrada al mundo, reconexión y cambio de zona.
- Redimensionar desde cada borde a 100 %, 125 % y 150 % de DPI; reiniciar y
  comprobar la persistencia.
- Resolver los metadatos reales del build soportado sin registrar contenido.
- Confirmar que un mensaje portugués produce una fila y que `Traducir` muestra
  el resultado sin quitar el foco a Farever.
- Ejecutar la suite sintética existente para punteros inválidos, Unicode,
  duplicados y cambio de `chatClient`.

## Aceptación

El cambio queda aceptado cuando el panel:

- Arranca en `360 × 280`, puede redimensionarse y recuerda su tamaño.
- Solo aparece dentro del mundo con Farever en primer plano.
- No figura en `Alt+Tab`.
- Lee los mensajes reales del build actual y ofrece `Traducir` en cada fila.
- Mantiene minimapa, DPS y controles existentes sin regresiones.

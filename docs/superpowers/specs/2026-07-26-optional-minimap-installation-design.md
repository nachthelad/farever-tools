# Farever Translate: instalación con minimapa opcional

Fecha: 2026-07-26  
Estado: aprobado para planificación

## Objetivo

Permitir que Farever Translate se instale y funcione sin Farever Minimap &
DPS, conservando a la vez la convivencia segura con las versiones compatibles
del minimapa.

La distribución seguirá siendo un único ZIP portable. El instalador detectará
la situación de `dinput8.dll` y elegirá automáticamente un modo seguro.

## Alcance

Este cambio incluye:

- instalación independiente cuando Farever no tiene `dinput8.dll`;
- instalación encadenada cuando existe un minimapa compatible;
- rechazo sin cambios cuando existe un `dinput8.dll` desconocido;
- manifiesto instalado v3 con modo explícito;
- migración de instalaciones v1/v2;
- actualización, rollback y desinstalación para ambos modos;
- documentación, pruebas y reemplazo de la beta aún no publicada.

No cambia:

- el proxy ni sus cinco exports;
- el host, el lector HashLink o el overlay;
- Google Cloud, credenciales, caché o traducciones;
- los controles Alt y F6;
- el inventario de archivos del ZIP;
- la política de builds compatibles de Farever.

## Detección inicial

Antes de modificar archivos, el instalador validará .NET 8 Desktop Runtime
x64, el build de Farever, el paquete y que Farever esté cerrado.

Después aplicará esta matriz:

| Estado de la carpeta de Farever | Resultado |
| --- | --- |
| No existe `dinput8.dll` | Instalar en modo `Standalone` |
| Existe un minimapa con SHA-256 permitido | Instalar en modo `ChainedMinimap` |
| Existe cualquier otro `dinput8.dll` | Cancelar sin modificar archivos |
| Existen restos de una instalación incompleta | Cancelar y explicar la recuperación |

El modo independiente necesita instalar nuestro proxy con el nombre
`dinput8.dll`. Por eso no puede coexistir de forma segura con otro archivo
desconocido que ya ocupe ese nombre.

## Proxy

El proxy actual ya cubre los dos modos:

1. intenta cargar por ruta absoluta `farever-minimap.dll`;
2. si no existe o no puede cargarse, reenvía a `dinput8.dll` de System32;
3. inicia `FareverTranslate.Host.exe`.

No se modificará su lógica para esta funcionalidad.

## Manifiesto instalado v3

El manifiesto incorporará:

- `FormatVersion: 3`;
- `InstallMode: "Standalone" | "ChainedMinimap"`;
- metadatos existentes de producto, fecha y build;
- archivos propios con nombre y SHA-256;
- datos del minimapa únicamente cuando el modo sea encadenado.

En `Standalone`, `MinimapVersion`, `OriginalDinput8Sha256`, `ChainedFile` y
`BackupFile` serán nulos. No se crearán directorios ni respaldos ficticios.

En `ChainedMinimap`, esos campos seguirán siendo obligatorios y apuntarán a:

- `farever-minimap.dll`;
- `FareverTranslate.Backup\dinput8.original.dll`.

Los manifiestos v1/v2 se interpretarán como instalaciones encadenadas. Una
actualización válida los migrará a v3 sin cambiar de modo.

## Instalación

### Modo independiente

1. Preparar y verificar el proxy y el host en un directorio temporal.
2. Confirmar nuevamente que no existe `dinput8.dll`.
3. Copiar los archivos propios.
4. Escribir atómicamente el manifiesto v3 `Standalone`.
5. Validar la instalación completa.

Si falla cualquier paso, se eliminarán solamente los archivos propios que
hayan sido creados y no quedarán manifiestos parciales.

### Modo con minimapa

Se conservará el flujo actual:

1. validar el hash permitido;
2. copiar el minimapa a un respaldo verificado byte por byte;
3. renombrarlo como `farever-minimap.dll`;
4. copiar el proxy y el host;
5. escribir el manifiesto v3 `ChainedMinimap`;
6. validar la instalación completa.

Ante un error se restaurará el minimapa en su nombre y contenido originales.

## Actualización y rollback

Una actualización mantendrá el `InstallMode` existente. No convertirá
automáticamente una instalación independiente en encadenada ni al revés.

Antes de reemplazar archivos:

- validará el manifiesto;
- verificará todos los hashes propios;
- en modo encadenado verificará también minimapa y respaldo;
- guardará archivos y manifiesto anteriores en el rollback temporal.

Si la actualización falla, restaurará la versión anterior. El rollback se
conservará únicamente si la restauración automática también falla.

Para cambiar de modo, el usuario deberá:

1. desinstalar Farever Translate;
2. instalar o retirar el minimapa;
3. reinstalar Farever Translate.

## Desinstalación

En ambos modos se rechazará la operación si Farever está abierto o si un
archivo propio no coincide con el manifiesto.

En `Standalone`:

- eliminar los archivos propios verificados;
- eliminar el manifiesto;
- no crear ni restaurar ningún `dinput8.dll`;
- Farever volverá a cargar DirectInput desde System32.

En `ChainedMinimap`:

- eliminar los archivos propios verificados;
- restaurar `dinput8.dll` desde `farever-minimap.dll`;
- comprobar su SHA-256 original;
- eliminar el respaldo solamente después de una restauración correcta;
- eliminar el manifiesto.

## Documentación

README, `LEEME.txt`, notas de release y mensajes del instalador presentarán el
minimapa como opcional.

La instalación principal tendrá tres pasos:

1. instalar .NET 8 Desktop Runtime x64;
2. extraer el ZIP y ejecutar `Instalar.cmd`;
3. abrir Farever y configurar la clave de Google.

Una sección separada explicará:

- convivencia con Farever Minimap & DPS v1.2.3/v1.2.4;
- rechazo de otros `dinput8.dll`;
- cambio de modo mediante desinstalación y reinstalación;
- recuperación ante archivos modificados.

## Pruebas

La suite automatizada cubrirá:

- instalación independiente desde una carpeta sin `dinput8.dll`;
- actualización independiente exitosa;
- rollback independiente ante fallo simulado;
- desinstalación independiente sin dejar archivos;
- instalación y restauración con minimapa;
- actualización encadenada;
- migración v1/v2 a v3 encadenado;
- rechazo de un `dinput8.dll` desconocido;
- conservación byte por byte del DLL rechazado;
- rechazo de archivos o manifiestos manipulados;
- contenido exacto del ZIP;
- versión single-file del host;
- arquitectura, exports y runtime estático del proxy;
- suite completa en CI y CodeQL.

La prueba manual previa a publicación validará el ZIP exacto descargado del
borrador de GitHub:

1. respaldar y verificar temporalmente el minimapa local;
2. instalar y probar el modo independiente en Farever real;
3. desinstalar y comprobar que no queda `dinput8.dll` local;
4. restaurar el minimapa byte por byte;
5. instalar y probar el modo encadenado;
6. validar minimapa/DPS, chat, traducción, Alt, F6, cargas y cambios de zona;
7. desinstalar y confirmar la restauración original;
8. dejar instalada nuevamente la combinación elegida por el usuario.

## Publicación

La release `v0.1.0-beta.1` actual sigue como borrador, no fue publicada y sus
assets tienen cero descargas.

Después de implementar y validar:

1. eliminar el borrador y el tag actuales;
2. publicar `main` con el cambio;
3. esperar CI y CodeQL;
4. recrear `v0.1.0-beta.1` sobre el nuevo commit;
5. descargar y verificar los assets generados por GitHub;
6. completar la prueba manual;
7. publicar la prerelease únicamente después de la aprobación del usuario.

## Criterios de aceptación

- Un usuario sin minimapa puede instalar, actualizar y desinstalar el
  traductor sin intervención manual.
- Un usuario con un minimapa permitido conserva minimapa y traductor.
- Un `dinput8.dll` desconocido nunca se sobrescribe, mueve ni elimina.
- Toda desinstalación afecta únicamente archivos demostrablemente propios.
- Las instalaciones v1/v2 actuales migran sin perder el minimapa.
- README y paquete no presentan el minimapa como requisito.
- La release pública se genera desde el commit que pasó la suite y CodeQL.

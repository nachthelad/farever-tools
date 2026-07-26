# Plan de implementación: minimapa opcional

1. Extender las pruebas de instalación para cubrir los modos `Standalone` y
   `ChainedMinimap`, migraciones v1/v2, rollback y DLL desconocidos.
2. Introducir el manifiesto instalado v3 y validarlo según su modo.
3. Adaptar instalación, actualización y desinstalación para conservar el modo
   y tocar únicamente archivos verificados.
4. Actualizar README, LEEME y notas de release para presentar el minimapa como
   integración opcional.
5. Ejecutar la suite completa, generar el paquete y revisar contenido,
   dependencias, exports, hashes, historial y secretos.
6. Reemplazar el borrador y tag no publicados, subir `main`, esperar CI y
   CodeQL, y recrear `v0.1.0-beta.1`.
7. Descargar el ZIP exacto de GitHub, verificarlo y probarlo en Farever real
   primero sin minimapa y finalmente con el minimapa restaurado.

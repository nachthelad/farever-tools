# Límites de seguridad

- El proxy solo encadena DirectInput, inicia el host una vez y no contiene
  lectura de memoria, red, gráficos ni credenciales.
- El host abre Farever con
  `PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`.
- No existe escritura de memoria, inyección, envío de teclas ni automatización
  del chat.
- Un hash no reconocido de `hlboot.dat` bloquea el lector antes de buscar
  objetos HashLink.
- Las direcciones se resuelven a partir de nombres y metadatos de tipos; no se
  guardan direcciones absolutas.
- La clave y el caché se cifran con DPAPI para el usuario actual.
- Los logs contienen estados, nombres de campos resueltos y tipos de error,
  nunca texto del chat ni credenciales.
- El instalador no distribuye el minimapa: trabaja sobre la copia existente y
  registra hashes para restaurarla.

La beta pública es para uso personal en Windows x64 y únicamente en builds
permitidos por hash. Es un proyecto comunitario no afiliado a Shiro Games y no
garantiza que Farever autorice su uso. Cada actualización del juego o del
minimapa requiere repetir las pruebas antes de ampliar la lista compatible.

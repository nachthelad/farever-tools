# Política de seguridad

## Versiones soportadas

| Versión | Soporte |
| --- | --- |
| 0.1.0-beta.1 | Sí |
| Builds anteriores privados | No |

## Reportar una vulnerabilidad

Usá **Private vulnerability reporting** en la pestaña Security del
repositorio. Si esa opción no está disponible, abrí un Issue sin incluir
detalles explotables y pedí un canal privado.

Incluí:

- versión de Farever Translate;
- versión de Windows;
- comportamiento observado y pasos mínimos para reproducirlo;
- hashes SHA-256 relevantes.

No adjuntes API keys, mensajes del chat, archivos DPAPI, cachés ni datos
personales. Los logs técnicos del proyecto no deberían contener ese contenido,
pero revisalos antes de publicarlos.

## Límites

Los binarios de la beta no están firmados y el proyecto interactúa con un
juego en Early Access. Un resultado limpio de antivirus no constituye una
garantía absoluta. La herramienta falla de forma cerrada ante hashes
desconocidos, evita escritura de memoria e inyección y no automatiza entradas.

Las vulnerabilidades confirmadas se corregirán en una nueva prerelease. No se
distribuyen actualizaciones silenciosas.

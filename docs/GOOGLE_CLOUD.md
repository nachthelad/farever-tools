# Configurar Google Cloud Translation

1. Crear o elegir un proyecto en Google Cloud Console.
2. Activar **Cloud Translation API**.
3. Crear una API key y restringirla a Cloud Translation API.
4. Configurar alertas de presupuesto y, si se desea, una cuota diaria de
   caracteres.
5. Abrir **Clave** en Farever Translate y pegarla.

El host usa Cloud Translation Basic v2 mediante
`POST /language/translate/v2`. Envía únicamente el texto de la fila cuyo botón
se pulsó, omite `source` para usar detección automática y fija `target` en
`es`. La clave y el caché se cifran con DPAPI para el usuario actual de
Windows. Los logs nunca incluyen mensajes ni credenciales.

Google publica un crédito mensual equivalente actualmente a los primeros
500.000 caracteres procesados. Los precios y créditos pueden cambiar: revisar
siempre la [tabla oficial](https://cloud.google.com/translate/pricing) antes de
activar facturación.

Referencia de la operación:
[Cloud Translation Basic v2](https://docs.cloud.google.com/translate/docs/reference/rest/v2/translate).

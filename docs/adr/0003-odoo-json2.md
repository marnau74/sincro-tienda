# ADR 3: Odoo con la API JSON-2 y una clave de API

**Estado:** aceptada · octubre de 2026

## Contexto

Odoo 19 trae la API externa JSON-2 (`POST /json/2/<modelo>/<método>`, argumentos por nombre,
`Authorization: bearer <clave>`) y marca como obsoletas XML-RPC y JSON-RPC (`/xmlrpc`, `/jsonrpc`),
que se retiran en Odoo 22 (otoño de 2028) y en Odoo Online 21.1.

## Decisión

Usar JSON-2 con la clave de API de un usuario dedicado a la integración, con los permisos justos
(ventas e inventario).

## Consecuencias

- Sin usuario y contraseña en la configuración; la clave se revoca o rota sin tocar la cuenta.
- Las claves duran como mucho tres meses: hay que rotarlas (Odoo permite generar la nueva con la
  anterior antes de revocarla).
- Cada llamada es una transacción propia: no se pueden encadenar varias en una. Por eso el pedido
  se crea con sus líneas en una sola llamada (`create` con comandos `(0, 0, valores)`).
- Errores: 401/403 y el resto de 4xx son permanentes (clave, permisos, datos rechazados); 5xx y
  429 se reintentan.

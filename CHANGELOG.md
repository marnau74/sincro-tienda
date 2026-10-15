# Cambios

Formato de [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); versiones con
[versionado semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Añadido

- Pedidos de WooCommerce a Odoo 19 (cliente, líneas por SKU y gastos de envío): presupuesto si está
  pendiente de pago, confirmado si está pagado y cancelado si se anula en la tienda.
- Stock disponible de Odoo a WooCommerce a partir de los movimientos de stock hechos.
- Webhook firmado, sondeo de respaldo de pedidos y cola en PostgreSQL con reintentos exponenciales,
  errores permanentes, un pendiente por entidad y orden por entidad.
- API de administración (resumen, fallidos, reintentos, registro de cambios, sincronización forzada).
- Demo completa con Docker (WooCommerce, Odoo 19 y el sincronizador) y CI.

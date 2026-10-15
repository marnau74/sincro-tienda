# ADR 1: la cola va en PostgreSQL, no en un broker

**Estado:** aceptada · octubre de 2026

## Contexto

Los cambios llegan por webhook (pedidos de WooCommerce) o se descubren sondeando (stock de Odoo,
pedidos cuyo webhook se perdió). Hay que guardarlos hasta poder llevarlos al otro sistema, con
reintentos cuando el destino falla. El volumen de una tienda pequeña o mediana es de decenas o
cientos de cambios al día.

## Decisión

Una tabla `mensajes` en el mismo PostgreSQL que el resto del estado, procesada con
`UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING`:

- varias instancias pueden procesar a la vez sin tomar el mismo mensaje (lo bloqueado se salta);
- cada mensaje tomado lleva un plazo (`bloqueado_hasta`); si la instancia muere, al vencer otro lo retoma;
- los reintentos son una fecha (`proximo_intento`) con espera exponencial y ±20 % al azar.

## Alternativas

- **RabbitMQ / Azure Service Bus.** Más piezas que desplegar y vigilar, y el estado quedaría en dos
  sitios (lo que hay en el broker y lo que hay en la base de datos), con el problema clásico de
  guardar en uno y fallar al publicar en el otro.
- **Hangfire / Quartz.** Resuelven la planificación pero no las reglas de orden y de duplicados
  por entidad, que habría que añadir igualmente.

## Consecuencias

- Una sola dependencia de infraestructura y todo se puede consultar con SQL (la API de
  administración es una vista de esta tabla).
- Escala hasta miles de mensajes por minuto, de sobra para el caso; si un día no basta, los
  puertos de la capa de aplicación permiten cambiar la cola sin tocar los casos de uso.

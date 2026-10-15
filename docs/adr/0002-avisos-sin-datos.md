# ADR 2: los mensajes son avisos de cambio y los datos se leen al procesarlos

**Estado:** aceptada · octubre de 2026

## Contexto

Los webhooks de WooCommerce pueden llegar repetidos, desordenados o no llegar. El sondeo vuelve a
ver cambios ya vistos. Si cada mensaje llevara los datos del pedido o del stock y se aplicara tal
cual, un mensaje viejo procesado tarde podría pisar uno más reciente.

## Decisión

Un mensaje solo dice «el pedido 1042 ha cambiado» o «el stock de CAF-250 ha cambiado». Al
procesarlo se lee el estado actual en la fuente (el pedido en WooCommerce, el stock disponible en
Odoo) y se compara con el destino antes de hacer nada (`ReglasPedido`).

Además, en la base de datos:

- `clave_idempotencia` única: el mismo cambio (entidad + versión) no se guarda dos veces;
- como mucho **un mensaje pendiente por entidad** (índice único parcial): si ya hay uno esperando,
  el nuevo sobra, porque el que espera leerá los datos al día;
- de una misma entidad no se toma un mensaje mientras otro más antiguo se está procesando.

## Consecuencias

- Repetir, reordenar o perder un aviso no cambia el resultado final. Un pedido se puede volver a
  sincronizar a mano en cualquier momento (`POST /api/pedidos/{id}/sincronizar`).
- Crear un pedido en Odoo no es idempotente por sí mismo: se busca antes por `client_order_ref`
  (`WC-<id>`). Si se cae justo después de crearlo, el siguiente intento lo encuentra y solo lo
  confirma. Por eso las peticiones a Odoo no se reintentan automáticamente en el cliente HTTP: los
  reintentos son de la cola, que vuelve a mirar antes de actuar.
- Cuesta una lectura más por mensaje, irrelevante a este volumen.

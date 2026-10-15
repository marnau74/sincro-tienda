# Sincro tienda

Sincronizador entre una tienda **WooCommerce** y el ERP **Odoo 19**: los pedidos de la web pasan a
Odoo como pedidos de venta (con su cliente) y el stock de Odoo vuelve a la web. Pensado para lo que
pasa de verdad en producción: webhooks repetidos, desordenados o perdidos, un ERP que se cae un
rato, un SKU que no existe en el ERP o dos instancias procesando a la vez.

**.NET 10 · ASP.NET Core · EF Core · PostgreSQL · API JSON-2 de Odoo 19 · API REST de WooCommerce ·
xUnit · Testcontainers · WireMock.Net · Docker**

> **In English.** Order and stock sync between a WooCommerce store and Odoo 19, built in .NET 10.
> Orders flow from the store to Odoo as sales orders (customer included) and available stock flows
> back. Webhooks are verified and queued in PostgreSQL (`FOR UPDATE SKIP LOCKED`), processed with
> exponential backoff, deduplicated and kept in order per entity; messages are change notices and the
> data is re-read from the source, so duplicates, reordering and lost webhooks don't change the result.
> Uses Odoo's new JSON-2 API with API keys. Unit, integration (real PostgreSQL in Docker, mocked APIs)
> and architecture tests, plus a full local demo with real Odoo and WooCommerce.

## Qué hace

**Pedidos: WooCommerce → Odoo**

- Un pedido pagado (`processing`, `completed`) se crea en Odoo con su cliente y se **confirma**
  (Odoo reserva el stock). Uno pendiente de pago (`pending`, `on-hold`) queda como **presupuesto**.
- Si se cancela o se reembolsa en la tienda, se **cancela** en Odoo.
- Los productos se emparejan por **SKU** = referencia interna de Odoo. Los gastos de envío van en
  una línea con el producto de servicio `ENVIO` (configurable). Los precios van sin impuestos y con
  los descuentos aplicados; los impuestos los pone Odoo según el producto.
- El cliente se busca por email y se crea si no existe.

**Stock: Odoo → WooCommerce**

- Cada pocos minutos se miran los movimientos de stock hechos en Odoo desde la última vez (ventas,
  compras, ajustes de inventario) y se pone en la tienda el **disponible** (a mano menos lo
  reservado) de cada producto afectado.

**Lo que hace que no falle**

- **Webhook firmado y rápido.** Se comprueba la firma HMAC-SHA256 de WooCommerce, se guarda un aviso en
  la cola y se responde en milisegundos (WooCommerce desactiva los webhooks que fallan).
- **Sondeo de respaldo.** Cada 10 minutos se revisan los pedidos modificados en la tienda por si se
  perdió algún webhook (caídas, reinicios, despliegues).
- **Cola en PostgreSQL** con reintentos exponenciales (30 s, 1 min, 2 min… hasta 1 h, con ±20 % al
  azar) y errores **permanentes** que no gastan reintentos (un SKU que no está en Odoo, una clave mal
  puesta): quedan como fallidos para que alguien los mire.
- **Idempotente.** Los mensajes son avisos («el pedido 1042 ha cambiado»): al procesarlos se lee el
  estado actual y se compara con Odoo antes de hacer nada. Repetidos, desordenados o perdidos, el
  resultado es el mismo. Si se cae justo después de crear el pedido, el siguiente intento solo lo
  confirma (se busca siempre por `client_order_ref` = `WC-<id>`).
- **Varias instancias** a la vez sin pisarse (`FOR UPDATE SKIP LOCKED`), en orden dentro de cada
  pedido o SKU, y con plazo de bloqueo para retomar lo que deje a medias una instancia que muere.
- **Registro de cambios** consultable: qué se hizo con cada pedido o SKU, cuándo y por qué falló.

## API

| Ruta | Para qué |
|---|---|
| `POST /webhooks/woocommerce` | Webhooks de pedidos (firmados) |
| `GET /api/resumen` | Mensajes por estado y hasta dónde se ha leído cada fuente |
| `GET /api/mensajes?estado=Fallido` | Lo que ha fallado y por qué |
| `POST /api/mensajes/{id}/reintentar` | Reintentar un fallido (p. ej. tras crear el SKU en Odoo) |
| `GET /api/registro?referencia=1042` | Historia de un pedido o de un SKU |
| `POST /api/pedidos/{id}/sincronizar` | Llevar un pedido a Odoo ahora |
| `POST /api/stock/detectar` | Buscar cambios de stock sin esperar |
| `GET /health` · `GET /openapi/v1.json` | Salud (incluye la base de datos) y OpenAPI |

Lo que va bajo `/api` pide la cabecera `X-Api-Key`.

## Cómo está hecho

```
src/
├── Sincro.Dominio/          Pedido de la tienda, reglas de decisión, mensaje de la cola y reintentos (sin dependencias)
├── Sincro.Aplicacion/       Casos de uso y puertos (ITienda, IErp, IColaMensajes...)
├── Sincro.Infraestructura/  WooCommerce (REST + OAuth 1.0a), Odoo (JSON-2), PostgreSQL (EF Core)
└── Sincro.Api/              Webhook, administración y tareas periódicas
tests/
├── Sincro.Unitarios.Tests/      Reglas, reintentos y casos de uso con dobles en memoria
├── Sincro.Integracion.Tests/    Cola y API contra PostgreSQL real (Testcontainers); WooCommerce y Odoo simulados (WireMock)
└── Sincro.Arquitectura.Tests/   Las dependencias entre capas van hacia dentro
```

Decisiones, con sus alternativas:

- [ADR 1](docs/adr/0001-cola-en-postgresql.md): la cola va en PostgreSQL, no en un broker.
- [ADR 2](docs/adr/0002-avisos-sin-datos.md): los mensajes son avisos y los datos se leen al procesarlos.
- [ADR 3](docs/adr/0003-odoo-json2.md): Odoo con la API JSON-2 y clave de API (XML-RPC y JSON-RPC se retiran en Odoo 22).

Detalles que conviene saber:

- **Firma de WooCommerce.** Por HTTPS se usa Basic con la clave y el secreto; por HTTP (la tienda
  local de la demo) WooCommerce exige OAuth 1.0a, que se firma con HMAC-SHA256 igual que su
  `class-wc-rest-authentication.php` (hay un test con un valor calculado aparte).
- **Reintentos HTTP.** Solo se reintentan en el momento las peticiones seguras de repetir (GET).
  Crear o confirmar un pedido no: si la respuesta se pierde, el siguiente intento de la cola mira
  antes qué hay en Odoo.
- **Límites conocidos.** Solo productos simples (las variaciones de WooCommerce no se sincronizan);
  una moneda; los reembolsos parciales no se llevan a Odoo.

## Demo completa en local

Hace falta Docker. Levanta WooCommerce, Odoo 19 y el sincronizador, crea los mismos productos en
los dos, las claves de API y los webhooks:

```bash
./demo/preparar.sh   # la primera vez tarda unos minutos (Odoo instala Ventas e Inventario)
./demo/pedido.sh     # crea un pedido pagado en la tienda y espera a verlo en Odoo
```

- Tienda: http://localhost:8080/wp-admin (`admin` / `admin-demo`)
- Odoo: http://localhost:8069 (`admin` / `admin`)
- Sincronizador: http://localhost:5080 (la clave de administración queda en `demo/.env`)

Para ver el stock en marcha, ajusta la cantidad de un producto en Odoo (Inventario › Productos ›
Actualizar cantidad) y en un minuto cambia en la tienda.

Son contraseñas de una demo local; nada de esto está pensado para exponerse a internet.

## Ejecutarlo contra una tienda y un Odoo reales

```bash
dotnet run --project src/Sincro.Api
```

Configuración (por variables de entorno, `__` separa secciones):

| Variable | |
|---|---|
| `ConnectionStrings__sincro` | PostgreSQL del sincronizador |
| `WooCommerce__Url`, `__ClaveCliente`, `__SecretoCliente`, `__SecretoWebhook` | Clave REST de WooCommerce (lectura y escritura) y secreto de los webhooks |
| `Odoo__Url`, `__BaseDeDatos`, `__ClaveApi`, `__SkuEnvio` | Clave de API de un usuario dedicado con permisos de ventas e inventario |
| `Administracion__ClaveApi` | Clave de la API de administración |
| `Sincronizacion__*` | Intervalos (`ProcesarColaCadaSegundos`, `BuscarStockCadaMinutos`, `RevisarPedidosCadaMinutos`) |

En WooCommerce se crean dos webhooks (`order.created` y `order.updated`) hacia
`https://<sincronizador>/webhooks/woocommerce` con el mismo secreto.

## Pruebas

```bash
dotnet test   # necesita Docker para PostgreSQL (Testcontainers)
```

## Licencia

[MIT](LICENSE)

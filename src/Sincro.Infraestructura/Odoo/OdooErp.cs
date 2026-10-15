using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sincro.Aplicacion;
using Sincro.Dominio;
using Sincro.Dominio.Pedidos;

namespace Sincro.Infraestructura.Odoo;

/// <summary>
/// Pedidos de venta (<c>sale.order</c>), clientes (<c>res.partner</c>) y stock en Odoo.
/// El pedido se identifica por <c>client_order_ref</c> = referencia de la tienda: antes de crear
/// siempre se busca, así que un reintento nunca lo duplica.
/// </summary>
public sealed class OdooErp(ClienteJson2 odoo, IOptions<OdooOpciones> opciones) : IErp
{
    private const string FormatoFecha = "yyyy-MM-dd HH:mm:ss"; // Odoo guarda las fechas en UTC sin zona
    private static readonly string[] CamposEstado = ["state"];
    private static readonly string[] CamposId = ["id"];
    private static readonly string[] CamposReferencia = ["default_code"];
    private static readonly string[] CamposMovimiento = ["product_id", "date"];
    private static readonly string[] CamposDisponible = ["free_qty"];

    public async Task<(long Id, EstadoPedidoErp Estado)?> BuscarPedido(string referencia, CancellationToken ct)
    {
        var filas = await odoo.Llamar("sale.order", "search_read", new
        {
            domain = new object[] { new object[] { "client_order_ref", "=", referencia } },
            fields = CamposEstado,
            limit = 1,
        }, ct);
        if (filas.GetArrayLength() == 0)
        {
            return null;
        }
        var fila = filas[0];
        return (fila.GetProperty("id").GetInt64(), EstadoDe(fila.GetProperty("state").GetString()));
    }

    public static EstadoPedidoErp EstadoDe(string? estado) => estado switch
    {
        "draft" or "sent" => EstadoPedidoErp.Presupuesto,
        "sale" or "done" => EstadoPedidoErp.Confirmado,
        "cancel" => EstadoPedidoErp.Cancelado,
        _ => EstadoPedidoErp.Presupuesto,
    };

    public async Task<long> CrearPedido(PedidoTienda pedido, CancellationToken ct)
    {
        var idCliente = await BuscarOCrearCliente(pedido.Cliente, ct);
        var skus = pedido.Lineas.Select(l => l.Sku).ToList();
        var envio = opciones.Value.SkuEnvio;
        if (pedido.GastosEnvio > 0)
        {
            skus.Add(envio);
        }
        var productos = await IdsDeProductos(skus, ct);
        var faltan = skus.Where(s => !productos.ContainsKey(s)).Distinct().ToList();
        if (faltan.Count > 0)
        {
            throw new ErrorSincronizacion(
                $"Pedido {pedido.Numero}: no existen en Odoo los productos con referencia {string.Join(", ", faltan)}", permanente: true);
        }

        var lineas = pedido.Lineas
            .Select(l => Linea(productos[l.Sku], l.Cantidad, l.PrecioUnitario))
            .ToList();
        if (pedido.GastosEnvio > 0)
        {
            lineas.Add(Linea(productos[envio], 1, pedido.GastosEnvio));
        }
        var ids = await odoo.Llamar("sale.order", "create", new
        {
            vals_list = new[]
            {
                new Dictionary<string, object?>
                {
                    ["partner_id"] = idCliente,
                    ["client_order_ref"] = pedido.Referencia,
                    ["origin"] = $"WooCommerce #{pedido.Numero}",
                    ["note"] = pedido.Nota,
                    ["order_line"] = lineas,
                },
            },
        }, ct);
        return ids[0].GetInt64();
    }

    // Comando (0, 0, valores) de Odoo: crear una línea nueva dentro del pedido
    private static object[] Linea(long idProducto, decimal cantidad, decimal precio) =>
        [0, 0, new { product_id = idProducto, product_uom_qty = cantidad, price_unit = precio }];

    private async Task<long> BuscarOCrearCliente(ClienteTienda cliente, CancellationToken ct)
    {
        var existentes = await odoo.Llamar("res.partner", "search_read", new
        {
            domain = new object[] { new object[] { "email", "=ilike", cliente.Email } },
            fields = CamposId,
            limit = 1,
        }, ct);
        if (existentes.GetArrayLength() > 0)
        {
            return existentes[0].GetProperty("id").GetInt64();
        }

        long? idPais = null;
        if (cliente.Direccion.Pais is { } pais)
        {
            var paises = await odoo.Llamar("res.country", "search_read", new
            {
                domain = new object[] { new object[] { "code", "=", pais.ToUpperInvariant() } },
                fields = CamposId,
                limit = 1,
            }, ct);
            idPais = paises.GetArrayLength() > 0 ? paises[0].GetProperty("id").GetInt64() : null;
        }
        var ids = await odoo.Llamar("res.partner", "create", new
        {
            vals_list = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = cliente.Nombre,
                    ["email"] = cliente.Email,
                    ["phone"] = cliente.Telefono,
                    ["company_name"] = cliente.Empresa,
                    ["street"] = cliente.Direccion.Calle,
                    ["city"] = cliente.Direccion.Ciudad,
                    ["zip"] = cliente.Direccion.CodigoPostal,
                    ["country_id"] = idPais,
                },
            },
        }, ct);
        return ids[0].GetInt64();
    }

    private async Task<Dictionary<string, long>> IdsDeProductos(IReadOnlyCollection<string> skus, CancellationToken ct)
    {
        var filas = await odoo.Llamar("product.product", "search_read", new
        {
            domain = new object[] { new object[] { "default_code", "in", skus.Distinct().ToArray() } },
            fields = CamposReferencia,
        }, ct);
        var res = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var f in filas.EnumerateArray())
        {
            if (f.GetProperty("default_code").ValueKind == JsonValueKind.String)
            {
                res.TryAdd(f.GetProperty("default_code").GetString()!, f.GetProperty("id").GetInt64());
            }
        }
        return res;
    }

    public Task ConfirmarPedido(long id, CancellationToken ct) =>
        odoo.Llamar("sale.order", "action_confirm", new { ids = new[] { id } }, ct);

    // Sin el aviso de cancelación (en la interfaz abre un asistente para avisar al cliente por correo)
    public Task CancelarPedido(long id, CancellationToken ct) =>
        odoo.Llamar("sale.order", "action_cancel", new { ids = new[] { id }, context = new { disable_cancel_warning = true } }, ct);

    public async Task<(IReadOnlyList<string> Skus, DateTimeOffset? UltimoCambio)> StockCambiadoDesde(DateTimeOffset desde, CancellationToken ct)
    {
        // Todo cambio de stock en Odoo (venta, compra, ajuste de inventario) deja un movimiento hecho
        var movimientos = await odoo.Llamar("stock.move", "search_read", new
        {
            domain = new object[]
            {
                new object[] { "state", "=", "done" },
                new object[] { "date", ">", desde.UtcDateTime.ToString(FormatoFecha, CultureInfo.InvariantCulture) },
            },
            fields = CamposMovimiento,
            order = "date asc",
            limit = 2000,
        }, ct);
        if (movimientos.GetArrayLength() == 0)
        {
            return ([], null);
        }

        var idsProducto = new HashSet<long>();
        DateTimeOffset? ultimo = null;
        foreach (var m in movimientos.EnumerateArray())
        {
            idsProducto.Add(m.GetProperty("product_id")[0].GetInt64()); // many2one: [id, nombre]
            var fecha = DateTime.ParseExact(m.GetProperty("date").GetString()!, FormatoFecha, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var f = new DateTimeOffset(fecha, TimeSpan.Zero);
            if (ultimo is null || f > ultimo)
            {
                ultimo = f;
            }
        }
        var productos = await odoo.Llamar("product.product", "read", new { ids = idsProducto.ToArray(), fields = CamposReferencia }, ct);
        var skus = productos.EnumerateArray()
            .Select(p => p.GetProperty("default_code"))
            .Where(c => c.ValueKind == JsonValueKind.String)
            .Select(c => c.GetString()!)
            .ToList();
        return (skus, ultimo);
    }

    public async Task<decimal?> LeerStock(string sku, CancellationToken ct)
    {
        // Disponible = a mano menos lo reservado para pedidos ya confirmados
        var filas = await odoo.Llamar("product.product", "search_read", new
        {
            domain = new object[] { new object[] { "default_code", "=", sku } },
            fields = CamposDisponible,
            limit = 1,
        }, ct);
        return filas.GetArrayLength() == 0 ? null : filas[0].GetProperty("free_qty").GetDecimal();
    }
}

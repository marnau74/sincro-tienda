using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Sincro.Aplicacion;
using Sincro.Dominio;
using Sincro.Dominio.Pedidos;

namespace Sincro.Infraestructura.WooCommerce;

/// <summary>API REST v3 de WooCommerce. Solo productos simples: las variaciones no se sincronizan.</summary>
public sealed class WooCommerceTienda(HttpClient http) : ITienda
{
    private const string Api = "wp-json/wc/v3/";

    public async Task<PedidoTienda?> ObtenerPedido(long id, CancellationToken ct)
    {
        using var r = await http.GetAsync($"{Api}orders/{id}", ct);
        if (r.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await ComprobarRespuesta(r, ct);
        var dto = await r.Content.ReadFromJsonAsync<PedidoWoo>(ct)
            ?? throw new ErrorSincronizacion($"La tienda devolvió el pedido {id} vacío", permanente: false);
        return dto.ADominio();
    }

    public async Task<IReadOnlyList<(long Id, DateTimeOffset ModificadoEn)>> PedidosModificadosDesde(
        DateTimeOffset desde, int pagina, int porPagina, CancellationToken ct)
    {
        var fecha = desde.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var url = $"{Api}orders?modified_after={fecha}&dates_are_gmt=true&orderby=modified&order=asc"
            + $"&per_page={porPagina}&page={pagina}&_fields=id,date_modified_gmt";
        using var r = await http.GetAsync(url, ct);
        await ComprobarRespuesta(r, ct);
        var lista = await r.Content.ReadFromJsonAsync<List<PedidoWoo>>(ct) ?? [];
        return [.. lista.Select(p => (p.Id, PedidoWoo.FechaGmt(p.ModificadoGmt)))];
    }

    public async Task<long?> BuscarProductoPorSku(string sku, CancellationToken ct)
    {
        using var r = await http.GetAsync($"{Api}products?sku={Uri.EscapeDataString(sku)}&_fields=id,sku", ct);
        await ComprobarRespuesta(r, ct);
        var lista = await r.Content.ReadFromJsonAsync<List<ProductoWoo>>(ct) ?? [];
        // La búsqueda por SKU de WooCommerce es exacta, pero se comprueba igualmente
        return lista.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.Ordinal))?.Id;
    }

    public async Task ActualizarStock(long idProducto, int cantidad, CancellationToken ct)
    {
        // Cuerpo entero con Content-Length (sin chunked), como en Odoo: no todos los servidores PHP lo leen
        using var cuerpo = JsonContent.Create(new { manage_stock = true, stock_quantity = cantidad });
        await cuerpo.LoadIntoBufferAsync(ct);
        using var r = await http.PutAsync($"{Api}products/{idProducto}", cuerpo, ct);
        await ComprobarRespuesta(r, ct);
    }

    /// <summary>
    /// 401/403: credenciales o permisos (no se arregla reintentando). 400/404: petición mal hecha.
    /// El resto (5xx, 429, tiempo agotado) es pasajero y se reintenta.
    /// </summary>
    private static async Task ComprobarRespuesta(HttpResponseMessage r, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode)
        {
            return;
        }
        var cuerpo = await r.Content.ReadAsStringAsync(ct);
        var codigo = (int)r.StatusCode;
        var permanente = codigo is >= 400 and < 500 and not 408 and not 429;
        var detalle = cuerpo.Length > 300 ? cuerpo[..300] : cuerpo;
        if (!permanente)
        {
            throw new HttpRequestException($"WooCommerce respondió {codigo}: {detalle}", null, r.StatusCode);
        }
        throw new ErrorSincronizacion($"WooCommerce respondió {codigo}: {detalle}", permanente: true);
    }
}

internal sealed class ProductoWoo
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("sku")] public string? Sku { get; set; }
}

internal sealed class PedidoWoo
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("number")] public string? Numero { get; set; }
    [JsonPropertyName("status")] public string? Estado { get; set; }
    [JsonPropertyName("date_modified_gmt")] public string? ModificadoGmt { get; set; }
    [JsonPropertyName("currency")] public string? Moneda { get; set; }
    [JsonPropertyName("shipping_total")] public string? TotalEnvio { get; set; }
    [JsonPropertyName("customer_note")] public string? Nota { get; set; }
    [JsonPropertyName("billing")] public FacturacionWoo? Facturacion { get; set; }
    [JsonPropertyName("line_items")] public List<LineaWoo> Lineas { get; set; } = [];

    public PedidoTienda ADominio()
    {
        var f = Facturacion ?? new FacturacionWoo();
        var nombre = $"{f.Nombre} {f.Apellidos}".Trim();
        var cliente = new ClienteTienda(
            f.Email?.Trim() ?? string.Empty,
            nombre.Length > 0 ? nombre : f.Empresa ?? f.Email ?? "Cliente de la tienda",
            Vacio(f.Telefono),
            Vacio(f.Empresa),
            new DireccionPostal(Vacio($"{f.Direccion1} {f.Direccion2}".Trim()), Vacio(f.Ciudad), Vacio(f.CodigoPostal), Vacio(f.Provincia), Vacio(f.Pais)));
        return new PedidoTienda(
            Id,
            Numero ?? Id.ToString(CultureInfo.InvariantCulture),
            EstadoDe(Estado),
            FechaGmt(ModificadoGmt),
            Moneda ?? "EUR",
            cliente,
            [.. Lineas.Select(l => l.ADominio())],
            Decimal(TotalEnvio),
            Vacio(Nota));
    }

    public static EstadoPedidoTienda EstadoDe(string? estado) => estado switch
    {
        "pending" => EstadoPedidoTienda.Pendiente,
        "on-hold" => EstadoPedidoTienda.EnEspera,
        "processing" => EstadoPedidoTienda.Procesando,
        "completed" => EstadoPedidoTienda.Completado,
        "cancelled" => EstadoPedidoTienda.Cancelado,
        "refunded" => EstadoPedidoTienda.Reembolsado,
        "failed" => EstadoPedidoTienda.Fallido,
        "checkout-draft" => EstadoPedidoTienda.Borrador,
        _ => EstadoPedidoTienda.Otro,
    };

    /// <summary>WooCommerce da las fechas <c>_gmt</c> sin zona: son UTC.</summary>
    public static DateTimeOffset FechaGmt(string? s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : DateTimeOffset.MinValue;

    public static decimal Decimal(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

internal sealed class FacturacionWoo
{
    [JsonPropertyName("first_name")] public string? Nombre { get; set; }
    [JsonPropertyName("last_name")] public string? Apellidos { get; set; }
    [JsonPropertyName("company")] public string? Empresa { get; set; }
    [JsonPropertyName("address_1")] public string? Direccion1 { get; set; }
    [JsonPropertyName("address_2")] public string? Direccion2 { get; set; }
    [JsonPropertyName("city")] public string? Ciudad { get; set; }
    [JsonPropertyName("state")] public string? Provincia { get; set; }
    [JsonPropertyName("postcode")] public string? CodigoPostal { get; set; }
    [JsonPropertyName("country")] public string? Pais { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("phone")] public string? Telefono { get; set; }
}

internal sealed class LineaWoo
{
    [JsonPropertyName("name")] public string? Nombre { get; set; }
    [JsonPropertyName("sku")] public string? Sku { get; set; }
    [JsonPropertyName("quantity")] public decimal Cantidad { get; set; }

    /// <summary>Total de la línea con descuentos y sin impuestos.</summary>
    [JsonPropertyName("total")] public string? Total { get; set; }

    public LineaPedido ADominio()
    {
        var total = PedidoWoo.Decimal(Total);
        var precio = Cantidad == 0 ? 0 : Math.Round(total / Cantidad, 4);
        return new LineaPedido(Sku?.Trim() ?? string.Empty, Nombre ?? string.Empty, Cantidad, precio);
    }
}

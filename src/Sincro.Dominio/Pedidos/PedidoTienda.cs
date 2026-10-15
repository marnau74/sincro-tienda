namespace Sincro.Dominio.Pedidos;

/// <summary>
/// Pedido tal como llega de la tienda online, ya traducido a términos propios.
/// Los importes de las líneas van sin impuestos: los impuestos los pone el ERP según el producto.
/// </summary>
public sealed record PedidoTienda(
    long Id,
    string Numero,
    EstadoPedidoTienda Estado,
    DateTimeOffset ModificadoEn,
    string Moneda,
    ClienteTienda Cliente,
    IReadOnlyList<LineaPedido> Lineas,
    decimal GastosEnvio,
    string? Nota)
{
    /// <summary>Referencia con la que se guarda en el ERP (p. ej. <c>WC-1042</c>) y que lo hace idempotente.</summary>
    public string Referencia => ReferenciaDe(Id);

    public static string ReferenciaDe(long id) => $"WC-{id}";

    /// <summary>Errores que impiden llevarlo al ERP (no se arreglan reintentando).</summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = new List<string>();
        if (string.IsNullOrWhiteSpace(Cliente.Email))
        {
            errores.Add("el pedido no tiene email de cliente");
        }
        if (Lineas.Count == 0)
        {
            errores.Add("el pedido no tiene líneas");
        }
        foreach (var l in Lineas)
        {
            if (string.IsNullOrWhiteSpace(l.Sku))
            {
                errores.Add($"la línea «{l.Nombre}» no tiene SKU");
            }
            if (l.Cantidad <= 0)
            {
                errores.Add($"la línea «{l.Nombre}» tiene una cantidad no válida ({l.Cantidad})");
            }
        }
        return errores;
    }
}

public sealed record ClienteTienda(string Email, string Nombre, string? Telefono, string? Empresa, DireccionPostal Direccion);

public sealed record DireccionPostal(string? Calle, string? Ciudad, string? CodigoPostal, string? Provincia, string? Pais);

/// <summary>Línea del pedido: el SKU es el que une el producto de la tienda con el del ERP.</summary>
public sealed record LineaPedido(string Sku, string Nombre, decimal Cantidad, decimal PrecioUnitario);

public enum EstadoPedidoTienda
{
    Pendiente,
    EnEspera,
    Procesando,
    Completado,
    Cancelado,
    Reembolsado,
    Fallido,
    Borrador,
    Otro,
}

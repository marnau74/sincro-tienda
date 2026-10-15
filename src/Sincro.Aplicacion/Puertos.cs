using Sincro.Dominio.Cola;
using Sincro.Dominio.Pedidos;
using Sincro.Dominio.Registro;

namespace Sincro.Aplicacion;

/// <summary>La tienda online (WooCommerce).</summary>
public interface ITienda
{
    /// <summary>El pedido tal como está ahora (null si ya no existe).</summary>
    Task<PedidoTienda?> ObtenerPedido(long id, CancellationToken ct);

    /// <summary>Pedidos modificados después de <paramref name="desde"/>, del más antiguo al más reciente, por páginas.</summary>
    Task<IReadOnlyList<(long Id, DateTimeOffset ModificadoEn)>> PedidosModificadosDesde(
        DateTimeOffset desde, int pagina, int porPagina, CancellationToken ct);

    /// <summary>Id del producto con ese SKU (null si la tienda no lo vende).</summary>
    Task<long?> BuscarProductoPorSku(string sku, CancellationToken ct);

    Task ActualizarStock(long idProducto, int cantidad, CancellationToken ct);
}

/// <summary>El ERP (Odoo).</summary>
public interface IErp
{
    Task<(long Id, EstadoPedidoErp Estado)?> BuscarPedido(string referencia, CancellationToken ct);

    /// <summary>Crea el pedido (y el cliente si no existe) como presupuesto; devuelve su id.</summary>
    Task<long> CrearPedido(PedidoTienda pedido, CancellationToken ct);

    Task ConfirmarPedido(long id, CancellationToken ct);

    Task CancelarPedido(long id, CancellationToken ct);

    /// <summary>SKU de los productos con movimientos de stock hechos después de <paramref name="desde"/> (UTC) y el más reciente.</summary>
    Task<(IReadOnlyList<string> Skus, DateTimeOffset? UltimoCambio)> StockCambiadoDesde(DateTimeOffset desde, CancellationToken ct);

    /// <summary>Unidades disponibles ahora de ese SKU (null si no existe en el ERP).</summary>
    Task<decimal?> LeerStock(string sku, CancellationToken ct);
}

/// <summary>Cola de mensajes guardada en la base de datos.</summary>
public interface IColaMensajes
{
    /// <summary>
    /// Guarda el mensaje salvo que sea repetido (misma clave de idempotencia) o que ya haya uno pendiente
    /// de la misma entidad (el que está esperando leerá los datos al día). Devuelve si se ha guardado.
    /// </summary>
    Task<bool> Encolar(Mensaje mensaje, CancellationToken ct);

    /// <summary>Toma hasta <paramref name="cuantos"/> mensajes listos, de entidades distintas, y los bloquea para este proceso.</summary>
    Task<IReadOnlyList<Mensaje>> TomarSiguientes(int cuantos, TimeSpan bloqueo, CancellationToken ct);

    Task Guardar(Mensaje mensaje, CancellationToken ct);

    Task<Mensaje?> Obtener(Guid id, CancellationToken ct);
}

public interface IRegistroCambios
{
    Task Anotar(RegistroCambio cambio, CancellationToken ct);
}

/// <summary>Hasta dónde se ha leído cada fuente (p. ej. la fecha del último movimiento de stock visto).</summary>
public interface ICursores
{
    Task<DateTimeOffset?> Leer(string nombre, CancellationToken ct);

    Task Guardar(string nombre, DateTimeOffset valor, CancellationToken ct);
}

/// <summary>Qué pedido de la tienda corresponde a qué pedido del ERP.</summary>
public interface IPedidosSincronizados
{
    Task<long?> IdEnErp(long idTienda, CancellationToken ct);

    Task Guardar(long idTienda, long idErp, string accion, DateTimeOffset ahora, CancellationToken ct);
}

using Sincro.Aplicacion;
using Sincro.Dominio.Cola;
using Sincro.Dominio.Pedidos;
using Sincro.Dominio.Registro;

namespace Sincro.Unitarios.Tests;

/// <summary>Tienda en memoria.</summary>
internal sealed class TiendaFalsa : ITienda
{
    public Dictionary<long, PedidoTienda> Pedidos { get; } = [];
    public Dictionary<string, long> Productos { get; } = [];
    public Dictionary<long, int> Stock { get; } = [];

    public Task<PedidoTienda?> ObtenerPedido(long id, CancellationToken ct) =>
        Task.FromResult(Pedidos.GetValueOrDefault(id));

    public Task<IReadOnlyList<(long Id, DateTimeOffset ModificadoEn)>> PedidosModificadosDesde(
        DateTimeOffset desde, int pagina, int porPagina, CancellationToken ct)
    {
        IReadOnlyList<(long, DateTimeOffset)> lista = [.. Pedidos.Values
            .Where(p => p.ModificadoEn > desde)
            .OrderBy(p => p.ModificadoEn)
            .Skip((pagina - 1) * porPagina)
            .Take(porPagina)
            .Select(p => (p.Id, p.ModificadoEn))];
        return Task.FromResult(lista);
    }

    public Task<long?> BuscarProductoPorSku(string sku, CancellationToken ct) =>
        Task.FromResult(Productos.TryGetValue(sku, out var id) ? id : (long?)null);

    public Task ActualizarStock(long idProducto, int cantidad, CancellationToken ct)
    {
        Stock[idProducto] = cantidad;
        return Task.CompletedTask;
    }
}

/// <summary>ERP en memoria que apunta las llamadas que recibe.</summary>
internal sealed class ErpFalso : IErp
{
    public Dictionary<string, (long Id, EstadoPedidoErp Estado)> Pedidos { get; } = [];
    public Dictionary<string, decimal> Stock { get; } = [];
    public List<string> Llamadas { get; } = [];
    public List<string> SkusCambiados { get; } = [];
    public DateTimeOffset? UltimoCambio { get; set; }
    public DateTimeOffset? ConsultadoDesde { get; private set; }
    public Exception? FallarAlCrear { get; set; }
    private long _siguienteId = 100;

    public Task<(long Id, EstadoPedidoErp Estado)?> BuscarPedido(string referencia, CancellationToken ct) =>
        Task.FromResult(Pedidos.TryGetValue(referencia, out var p) ? p : ((long, EstadoPedidoErp)?)null);

    public Task<long> CrearPedido(PedidoTienda pedido, CancellationToken ct)
    {
        if (FallarAlCrear is not null)
        {
            throw FallarAlCrear;
        }
        var id = _siguienteId++;
        Pedidos[pedido.Referencia] = (id, EstadoPedidoErp.Presupuesto);
        Llamadas.Add($"crear {pedido.Referencia}");
        return Task.FromResult(id);
    }

    public Task ConfirmarPedido(long id, CancellationToken ct) => Cambiar(id, EstadoPedidoErp.Confirmado, "confirmar");

    public Task CancelarPedido(long id, CancellationToken ct) => Cambiar(id, EstadoPedidoErp.Cancelado, "cancelar");

    private Task Cambiar(long id, EstadoPedidoErp estado, string llamada)
    {
        var referencia = Pedidos.Single(p => p.Value.Id == id).Key;
        Pedidos[referencia] = (id, estado);
        Llamadas.Add($"{llamada} {id}");
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<string> Skus, DateTimeOffset? UltimoCambio)> StockCambiadoDesde(DateTimeOffset desde, CancellationToken ct)
    {
        ConsultadoDesde = desde;
        return Task.FromResult<(IReadOnlyList<string>, DateTimeOffset?)>((SkusCambiados, UltimoCambio));
    }

    public Task<decimal?> LeerStock(string sku, CancellationToken ct) =>
        Task.FromResult(Stock.TryGetValue(sku, out var c) ? c : (decimal?)null);
}

/// <summary>Cola en memoria con las mismas reglas de duplicados que la de PostgreSQL.</summary>
internal sealed class ColaFalsa : IColaMensajes
{
    public List<Mensaje> Mensajes { get; } = [];

    public Task<bool> Encolar(Mensaje mensaje, CancellationToken ct)
    {
        var repetido = Mensajes.Any(m => m.ClaveIdempotencia == mensaje.ClaveIdempotencia
            || (m.ClaveOrden == mensaje.ClaveOrden && m.Estado == EstadoMensaje.Pendiente));
        if (!repetido)
        {
            Mensajes.Add(mensaje);
        }
        return Task.FromResult(!repetido);
    }

    public Task<IReadOnlyList<Mensaje>> TomarSiguientes(int cuantos, TimeSpan bloqueo, CancellationToken ct)
    {
        IReadOnlyList<Mensaje> lote = [.. Mensajes.Where(m => m.Estado == EstadoMensaje.Pendiente).Take(cuantos)];
        foreach (var m in lote)
        {
            m.Tomar(DateTimeOffset.UtcNow, bloqueo);
        }
        return Task.FromResult(lote);
    }

    public Task Guardar(Mensaje mensaje, CancellationToken ct) => Task.CompletedTask;

    public Task<Mensaje?> Obtener(Guid id, CancellationToken ct) => Task.FromResult(Mensajes.FirstOrDefault(m => m.Id == id));
}

internal sealed class RegistroFalso : IRegistroCambios
{
    public List<RegistroCambio> Cambios { get; } = [];

    public Task Anotar(RegistroCambio cambio, CancellationToken ct)
    {
        Cambios.Add(cambio);
        return Task.CompletedTask;
    }
}

internal sealed class CursoresFalsos : ICursores
{
    public Dictionary<string, DateTimeOffset> Valores { get; } = [];

    public Task<DateTimeOffset?> Leer(string nombre, CancellationToken ct) =>
        Task.FromResult(Valores.TryGetValue(nombre, out var v) ? v : (DateTimeOffset?)null);

    public Task Guardar(string nombre, DateTimeOffset valor, CancellationToken ct)
    {
        Valores[nombre] = valor;
        return Task.CompletedTask;
    }
}

internal sealed class PedidosSincronizadosFalsos : IPedidosSincronizados
{
    public Dictionary<long, (long IdErp, string Accion)> Filas { get; } = [];

    public Task<long?> IdEnErp(long idTienda, CancellationToken ct) =>
        Task.FromResult(Filas.TryGetValue(idTienda, out var f) ? f.IdErp : (long?)null);

    public Task Guardar(long idTienda, long idErp, string accion, DateTimeOffset ahora, CancellationToken ct)
    {
        Filas[idTienda] = (idErp, accion);
        return Task.CompletedTask;
    }
}

internal static class Ejemplos
{
    public static PedidoTienda Pedido(long id = 1042, EstadoPedidoTienda estado = EstadoPedidoTienda.Procesando, string sku = "CAF-250",
        DateTimeOffset? modificado = null) => new(
        id,
        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        estado,
        modificado ?? new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero),
        "EUR",
        new ClienteTienda("ana@ejemplo.es", "Ana Pérez", "600000000", null, new DireccionPostal("Calle Mayor 1", "Murcia", "30001", "MU", "ES")),
        [new LineaPedido(sku, "Café molido 250 g", 2, 3.5m)],
        4.95m,
        null);
}

internal static class Contexto
{
    /// <summary>Se cancela si el test se interrumpe.</summary>
    public static CancellationToken Ct => TestContext.Current.CancellationToken;
}

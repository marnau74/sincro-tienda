using System.Globalization;
using Sincro.Dominio.Cola;

namespace Sincro.Aplicacion.Deteccion;

/// <summary>
/// Busca cambios sin esperar a que nadie avise y los mete en la cola:
/// <list type="bullet">
/// <item>Stock: los movimientos hechos en el ERP desde la última vez.</item>
/// <item>Pedidos: los modificados en la tienda, por si se perdió algún webhook (caídas, reinicios).</item>
/// </list>
/// Se relee un pequeño solape con la vez anterior: lo repetido se descarta en la cola, y así no se
/// pierde lo que se guardó en el mismo segundo que el último cambio visto.
/// </summary>
public sealed class DetectarCambios(ITienda tienda, IErp erp, IColaMensajes cola, ICursores cursores, TimeProvider reloj)
{
    public const string CursorStock = "erp_stock";
    public const string CursorPedidos = "tienda_pedidos";
    public static readonly TimeSpan Solape = TimeSpan.FromMinutes(2);

    /// <summary>La primera vez se mira el último día (no todo el histórico).</summary>
    public static readonly TimeSpan PrimeraVez = TimeSpan.FromDays(1);

    public async Task<int> Stock(CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var cursor = await cursores.Leer(CursorStock, ct) ?? ahora - PrimeraVez;
        var (skus, ultimo) = await erp.StockCambiadoDesde(cursor - Solape, ct);
        var version = (ultimo ?? ahora).ToString("O", CultureInfo.InvariantCulture);
        var encolados = 0;
        foreach (var sku in skus.Distinct())
        {
            if (await cola.Encolar(Mensaje.Nuevo(TipoMensaje.Stock, sku, version, ahora), ct))
            {
                encolados++;
            }
        }
        if (ultimo is not null && ultimo > cursor)
        {
            await cursores.Guardar(CursorStock, ultimo.Value, ct);
        }
        return encolados;
    }

    public async Task<int> Pedidos(CancellationToken ct, int porPagina = 50)
    {
        var ahora = reloj.GetUtcNow();
        var cursor = await cursores.Leer(CursorPedidos, ct) ?? ahora - PrimeraVez;
        var maximo = cursor;
        var encolados = 0;
        for (var pagina = 1; ; pagina++)
        {
            var lista = await tienda.PedidosModificadosDesde(cursor - Solape, pagina, porPagina, ct);
            foreach (var (id, modificado) in lista)
            {
                if (await cola.Encolar(EncolarPedido.Mensaje(id, modificado, ahora), ct))
                {
                    encolados++;
                }
                if (modificado > maximo)
                {
                    maximo = modificado;
                }
            }
            if (lista.Count < porPagina)
            {
                break;
            }
        }
        if (maximo > cursor)
        {
            await cursores.Guardar(CursorPedidos, maximo, ct);
        }
        return encolados;
    }
}

/// <summary>Mensaje de «este pedido ha cambiado» (lo usan el webhook y el sondeo).</summary>
public static class EncolarPedido
{
    public static Mensaje Mensaje(long id, DateTimeOffset modificadoEn, DateTimeOffset ahora) =>
        Dominio.Cola.Mensaje.Nuevo(TipoMensaje.Pedido, id.ToString(CultureInfo.InvariantCulture),
            modificadoEn.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), ahora);
}

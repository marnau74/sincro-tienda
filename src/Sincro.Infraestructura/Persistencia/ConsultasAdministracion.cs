using Microsoft.EntityFrameworkCore;
using Sincro.Dominio.Cola;

namespace Sincro.Infraestructura.Persistencia;

public sealed record MensajeVista(
    Guid Id, string Tipo, string Referencia, string Estado, int Intentos, DateTimeOffset ProximoIntento,
    string? UltimoError, DateTimeOffset CreadoEn, DateTimeOffset ActualizadoEn);

public sealed record CambioVista(DateTimeOffset Fecha, string Entidad, string Referencia, string Accion, string Detalle);

public sealed record Resumen(IReadOnlyDictionary<string, int> MensajesPorEstado, DateTimeOffset? UltimoStockLeido, DateTimeOffset? UltimoPedidoLeido);

/// <summary>Lo que consulta la API de administración (solo lectura).</summary>
public sealed class ConsultasAdministracion(SincroDbContext db)
{
    public async Task<IReadOnlyList<MensajeVista>> Mensajes(EstadoMensaje? estado, int limite, CancellationToken ct)
    {
        var q = db.Mensajes.AsNoTracking();
        if (estado is not null)
        {
            q = q.Where(m => m.Estado == estado);
        }
        return await q.OrderByDescending(m => m.ActualizadoEn).Take(Math.Clamp(limite, 1, 500))
            .Select(m => new MensajeVista(m.Id, m.Tipo.ToString(), m.Referencia, m.Estado.ToString(), m.Intentos,
                m.ProximoIntento, m.UltimoError, m.CreadoEn, m.ActualizadoEn))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CambioVista>> Registro(string? referencia, int limite, CancellationToken ct)
    {
        var q = db.Registro.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(referencia))
        {
            q = q.Where(r => r.Referencia == referencia);
        }
        return await q.OrderByDescending(r => r.Id).Take(Math.Clamp(limite, 1, 500))
            .Select(r => new CambioVista(r.Fecha, r.Entidad, r.Referencia, r.Accion, r.Detalle))
            .ToListAsync(ct);
    }

    public async Task<Resumen> Resumen(CancellationToken ct)
    {
        var porEstado = await db.Mensajes.AsNoTracking().GroupBy(m => m.Estado)
            .Select(g => new { g.Key, Total = g.Count() }).ToListAsync(ct);
        var cursores = await db.Cursores.AsNoTracking().ToDictionaryAsync(c => c.Nombre, c => c.Valor, ct);
        return new Resumen(
            Enum.GetValues<EstadoMensaje>().ToDictionary(e => e.ToString(), e => porEstado.FirstOrDefault(p => p.Key == e)?.Total ?? 0),
            cursores.TryGetValue(Aplicacion.Deteccion.DetectarCambios.CursorStock, out var s) ? s : null,
            cursores.TryGetValue(Aplicacion.Deteccion.DetectarCambios.CursorPedidos, out var p) ? p : null);
    }
}

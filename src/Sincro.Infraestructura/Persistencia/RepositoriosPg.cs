using Microsoft.EntityFrameworkCore;

using Sincro.Aplicacion;
using Sincro.Dominio.Registro;

namespace Sincro.Infraestructura.Persistencia;

public sealed class RegistroCambiosPg(SincroDbContext db) : IRegistroCambios
{
    public async Task Anotar(RegistroCambio cambio, CancellationToken ct)
    {
        db.Registro.Add(cambio);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class CursoresPg(SincroDbContext db) : ICursores
{
    public async Task<DateTimeOffset?> Leer(string nombre, CancellationToken ct) =>
        (await db.Cursores.AsNoTracking().FirstOrDefaultAsync(c => c.Nombre == nombre, ct))?.Valor;

    public Task Guardar(string nombre, DateTimeOffset valor, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cursores (nombre, valor) VALUES ({nombre}, {valor})
            ON CONFLICT (nombre) DO UPDATE SET valor = GREATEST(cursores.valor, EXCLUDED.valor)
            """, ct);
}

public sealed class PedidosSincronizadosPg(SincroDbContext db) : IPedidosSincronizados
{
    public async Task<long?> IdEnErp(long idTienda, CancellationToken ct) =>
        (await db.PedidosSincronizados.AsNoTracking().FirstOrDefaultAsync(p => p.IdTienda == idTienda, ct))?.IdErp;

    public Task Guardar(long idTienda, long idErp, string accion, DateTimeOffset ahora, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO pedidos_sincronizados (id_tienda, id_erp, ultima_accion, actualizado_en)
            VALUES ({idTienda}, {idErp}, {accion}, {ahora})
            ON CONFLICT (id_tienda) DO UPDATE SET id_erp = EXCLUDED.id_erp, ultima_accion = EXCLUDED.ultima_accion,
                                                  actualizado_en = EXCLUDED.actualizado_en
            """, ct);
}

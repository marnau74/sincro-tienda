using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sincro.Aplicacion;
using Sincro.Dominio.Cola;

namespace Sincro.Infraestructura.Persistencia;

/// <summary>
/// Cola sobre una tabla de PostgreSQL. Varias instancias pueden procesarla a la vez: cada una toma
/// filas con <c>FOR UPDATE SKIP LOCKED</c> (lo bloqueado por otra se salta) y las marca con un plazo;
/// si una instancia muere a mitad, al vencer el plazo otra las retoma.
/// </summary>
public sealed class ColaMensajesPg(SincroDbContext db, TimeProvider reloj) : IColaMensajes
{
    public async Task<bool> Encolar(Mensaje mensaje, CancellationToken ct)
    {
        // ON CONFLICT DO NOTHING cubre los dos índices únicos (repetido o ya hay uno pendiente de la entidad)
        // sin carreras entre instancias: lo decide PostgreSQL al insertar.
        var filas = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mensajes (id, tipo, clave_idempotencia, clave_orden, referencia, estado, intentos,
                                  proximo_intento, bloqueado_hasta, ultimo_error, creado_en, actualizado_en)
            VALUES ({mensaje.Id}, {mensaje.Tipo.ToString()}, {mensaje.ClaveIdempotencia}, {mensaje.ClaveOrden},
                    {mensaje.Referencia}, {mensaje.Estado.ToString()}, {mensaje.Intentos}, {mensaje.ProximoIntento},
                    NULL, NULL, {mensaje.CreadoEn}, {mensaje.ActualizadoEn})
            ON CONFLICT DO NOTHING
            """, ct);
        return filas == 1;
    }

    public async Task<IReadOnlyList<Mensaje>> TomarSiguientes(int cuantos, TimeSpan bloqueo, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var hasta = ahora + bloqueo;
        // Listos: pendientes cuyo turno ha llegado, o en proceso con el plazo vencido (su proceso murió).
        // Orden: no se toma uno si hay otro más antiguo de la misma entidad aún sin terminar.
        return await db.Mensajes.FromSqlInterpolated($"""
            UPDATE mensajes m
               SET estado = 'Procesando', bloqueado_hasta = {hasta}, actualizado_en = {ahora}
             WHERE m.id IN (
                   SELECT c.id FROM mensajes c
                    WHERE ((c.estado = 'Pendiente' AND c.proximo_intento <= {ahora})
                        OR (c.estado = 'Procesando' AND c.bloqueado_hasta < {ahora}))
                      AND NOT EXISTS (
                          SELECT 1 FROM mensajes o
                           WHERE o.clave_orden = c.clave_orden
                             AND o.creado_en < c.creado_en
                             AND (o.estado = 'Procesando' OR (o.estado = 'Pendiente' AND o.id <> c.id)))
                    ORDER BY c.creado_en
                    LIMIT {cuantos}
                    FOR UPDATE SKIP LOCKED)
            RETURNING m.*
            """).ToListAsync(ct);
    }

    public async Task Guardar(Mensaje mensaje, CancellationToken ct)
    {
        if (db.Entry(mensaje).State == EntityState.Detached)
        {
            db.Mensajes.Update(mensaje);
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<Mensaje?> Obtener(Guid id, CancellationToken ct) => db.Mensajes.FirstOrDefaultAsync(m => m.Id == id, ct);

    /// <summary>Un reintento a mano puede chocar con otro pendiente de la misma entidad: entonces no hace falta.</summary>
    public static bool EsChoqueDePendiente(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_mensajes_un_pendiente_por_entidad" };
}

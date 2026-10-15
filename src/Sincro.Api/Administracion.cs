using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Sincro.Aplicacion;
using Sincro.Aplicacion.Deteccion;
using Sincro.Dominio.Cola;
using Sincro.Infraestructura.Persistencia;

namespace Sincro.Api;

public sealed class OpcionesAdministracion
{
    public const string Seccion = "Administracion";

    /// <summary>Clave para la API de administración (cabecera <c>X-Api-Key</c>). Vacía: desactivada.</summary>
    public string ClaveApi { get; set; } = string.Empty;
}

/// <summary>
/// API para quien atiende la integración: ver qué ha fallado y por qué, reintentar y forzar una
/// sincronización. Protegida con una clave en <c>X-Api-Key</c>.
/// </summary>
public static class Administracion
{
    public static void MapAdministracion(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(ComprobarClave).WithTags("Administración");

        api.MapGet("/resumen", (ConsultasAdministracion q, CancellationToken ct) => q.Resumen(ct))
            .WithSummary("Mensajes por estado y hasta dónde se ha leído cada fuente");

        api.MapGet("/mensajes", (ConsultasAdministracion q, EstadoMensaje? estado, int? limite, CancellationToken ct) =>
                q.Mensajes(estado, limite ?? 50, ct))
            .WithSummary("Mensajes de la cola (p. ej. ?estado=Fallido)");

        api.MapPost("/mensajes/{id:guid}/reintentar", Reintentar)
            .WithSummary("Vuelve a poner en la cola un mensaje fallido");

        api.MapGet("/registro", (ConsultasAdministracion q, string? referencia, int? limite, CancellationToken ct) =>
                q.Registro(referencia, limite ?? 100, ct))
            .WithSummary("Registro de cambios (p. ej. ?referencia=1042 para un pedido o un SKU)");

        api.MapPost("/pedidos/{id:long}/sincronizar", async (long id, IColaMensajes cola, TimeProvider reloj, CancellationToken ct) =>
            {
                var ahora = reloj.GetUtcNow();
                var m = Mensaje.Nuevo(TipoMensaje.Pedido, id.ToString(CultureInfo.InvariantCulture), $"manual:{ahora:O}", ahora);
                var encolado = await cola.Encolar(m, ct);
                return Results.Accepted(value: new { encolado, motivo = encolado ? null : "ya hay uno pendiente de ese pedido" });
            })
            .WithSummary("Lleva un pedido al ERP ahora (p. ej. tras arreglar un SKU)");

        api.MapPost("/stock/detectar", async (DetectarCambios detectar, CancellationToken ct) =>
                Results.Ok(new { encolados = await detectar.Stock(ct) }))
            .WithSummary("Busca ahora los cambios de stock en el ERP sin esperar a la siguiente pasada");
    }

    private static async Task<IResult> Reintentar(Guid id, IColaMensajes cola, TimeProvider reloj, CancellationToken ct)
    {
        var m = await cola.Obtener(id, ct);
        if (m is null)
        {
            return Results.NotFound();
        }
        if (!m.Reintentar(reloj.GetUtcNow()))
        {
            return Results.Conflict(new { error = $"Solo se reintentan los mensajes fallidos (este está {m.Estado})" });
        }
        try
        {
            await cola.Guardar(m, ct);
        }
        catch (DbUpdateException e) when (ColaMensajesPg.EsChoqueDePendiente(e))
        {
            return Results.Conflict(new { error = "Ya hay un mensaje pendiente de esa entidad: leerá los datos al día" });
        }
        return Results.Ok(new { m.Id, estado = m.Estado.ToString() });
    }

    private static async ValueTask<object?> ComprobarClave(EndpointFilterInvocationContext ctx, EndpointFilterDelegate siguiente)
    {
        var clave = ctx.HttpContext.RequestServices.GetRequiredService<IOptions<OpcionesAdministracion>>().Value.ClaveApi;
        if (string.IsNullOrEmpty(clave))
        {
            return Results.Problem("La API de administración está desactivada (falta Administracion:ClaveApi)",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        var recibida = ctx.HttpContext.Request.Headers["X-Api-Key"].ToString();
        var iguales = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(recibida)), SHA256.HashData(Encoding.UTF8.GetBytes(clave)));
        return iguales ? await siguiente(ctx) : Results.Unauthorized();
    }
}

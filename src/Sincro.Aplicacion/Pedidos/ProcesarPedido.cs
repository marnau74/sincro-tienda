using Microsoft.Extensions.Logging;

using Sincro.Dominio;
using Sincro.Dominio.Pedidos;
using Sincro.Dominio.Registro;

namespace Sincro.Aplicacion.Pedidos;

/// <summary>
/// Lleva un pedido de la tienda al ERP. Lee siempre el pedido tal como está ahora y lo compara con
/// lo que hay en el ERP (<see cref="ReglasPedido"/>), así que es seguro repetirlo: si se cae a mitad
/// (creado pero sin confirmar), el siguiente intento solo confirma.
/// </summary>
public sealed partial class ProcesarPedido(
    ITienda tienda,
    IErp erp,
    IPedidosSincronizados pedidos,
    IRegistroCambios registro,
    TimeProvider reloj,
    ILogger<ProcesarPedido> log)
{
    public async Task<AccionErp> Ejecutar(long id, CancellationToken ct)
    {
        var pedido = await tienda.ObtenerPedido(id, ct);
        if (pedido is null)
        {
            await Anotar(id, "no_existe", "El pedido ya no existe en la tienda", ct);
            return AccionErp.Ninguna;
        }

        var existente = await erp.BuscarPedido(pedido.Referencia, ct);
        var estadoErp = existente?.Estado ?? EstadoPedidoErp.NoExiste;
        var accion = ReglasPedido.AccionPara(pedido.Estado, estadoErp);

        if (accion is AccionErp.CrearPresupuesto or AccionErp.CrearYConfirmar)
        {
            var errores = pedido.Validar();
            if (errores.Count > 0)
            {
                throw new ErrorSincronizacion($"Pedido {pedido.Numero}: {string.Join("; ", errores)}", permanente: true);
            }
        }

        long? idErp = existente?.Id;
        switch (accion)
        {
            case AccionErp.CrearPresupuesto:
                idErp = await erp.CrearPedido(pedido, ct);
                break;
            case AccionErp.CrearYConfirmar:
                idErp = await erp.CrearPedido(pedido, ct);
                await erp.ConfirmarPedido(idErp.Value, ct);
                break;
            case AccionErp.Confirmar:
                await erp.ConfirmarPedido(idErp!.Value, ct);
                break;
            case AccionErp.Cancelar:
                await erp.CancelarPedido(idErp!.Value, ct);
                break;
            case AccionErp.Ninguna:
                break;
        }

        var nombre = NombreAccion(accion);
        if (idErp is not null)
        {
            await pedidos.Guardar(id, idErp.Value, nombre, reloj.GetUtcNow(), ct);
        }
        await Anotar(id, nombre,
            $"Pedido {pedido.Numero} ({pedido.Estado}) · en el ERP: {estadoErp}{(idErp is null ? string.Empty : $" · id ERP {idErp}")}", ct);
        if (accion != AccionErp.Ninguna)
        {
            LogPedido(log, pedido.Numero, nombre, idErp);
        }
        return accion;
    }

    public static string NombreAccion(AccionErp accion) => accion switch
    {
        AccionErp.CrearPresupuesto => "crear_presupuesto",
        AccionErp.CrearYConfirmar => "crear_y_confirmar",
        AccionErp.Confirmar => "confirmar",
        AccionErp.Cancelar => "cancelar",
        _ => "sin_cambios",
    };

    private Task Anotar(long id, string accion, string detalle, CancellationToken ct) =>
        registro.Anotar(RegistroCambio.Nuevo(reloj.GetUtcNow(), "pedido", id.ToString(System.Globalization.CultureInfo.InvariantCulture), accion, detalle), ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido {Numero}: {Accion} (ERP {IdErp})")]
    private static partial void LogPedido(ILogger log, string numero, string accion, long? idErp);
}

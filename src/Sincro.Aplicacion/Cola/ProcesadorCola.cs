using System.Globalization;
using Microsoft.Extensions.Logging;
using Sincro.Aplicacion.Pedidos;
using Sincro.Aplicacion.Stock;
using Sincro.Dominio;
using Sincro.Dominio.Cola;
using Sincro.Dominio.Registro;

namespace Sincro.Aplicacion.Cola;

/// <summary>
/// Procesa un lote de la cola: cada mensaje acaba hecho, de vuelta en la cola con una espera
/// (error pasajero) o fallido (error permanente o sin más reintentos), y todo queda en el registro.
/// </summary>
public sealed partial class ProcesadorCola(
    IColaMensajes cola,
    ProcesarPedido pedidos,
    ProcesarStock stock,
    IRegistroCambios registro,
    TimeProvider reloj,
    ILogger<ProcesadorCola> log)
{
    public static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(5);
    public PoliticaReintentos Politica { get; init; } = PoliticaReintentos.PorDefecto;

    /// <summary>Devuelve cuántos mensajes ha tomado (0: la cola está al día).</summary>
    public async Task<int> ProcesarLote(int cuantos, CancellationToken ct)
    {
        var lote = await cola.TomarSiguientes(cuantos, Bloqueo, ct);
        foreach (var mensaje in lote)
        {
            await Procesar(mensaje, ct);
        }
        return lote.Count;
    }

    private async Task Procesar(Mensaje mensaje, CancellationToken ct)
    {
        try
        {
            switch (mensaje.Tipo)
            {
                case TipoMensaje.Pedido:
                    await pedidos.Ejecutar(long.Parse(mensaje.Referencia, CultureInfo.InvariantCulture), ct);
                    break;
                case TipoMensaje.Stock:
                    await stock.Ejecutar(mensaje.Referencia, ct);
                    break;
            }
            mensaje.MarcarHecho(reloj.GetUtcNow());
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Lo que no es un error conocido como permanente (red, tiempo agotado, 5xx...) se reintenta
            var permanente = e is ErrorSincronizacion { Permanente: true };
            mensaje.RegistrarFallo(e.Message, permanente, reloj.GetUtcNow(), Politica, Random.Shared.NextDouble());
            var final = mensaje.Estado == EstadoMensaje.Fallido;
            LogFallo(log, mensaje.ClaveOrden, mensaje.Intentos, final, e);
            await registro.Anotar(RegistroCambio.Nuevo(reloj.GetUtcNow(), mensaje.Tipo.ToString().ToLowerInvariant(),
                mensaje.Referencia, final ? "fallido" : "error_reintentara",
                final ? e.Message : $"{e.Message} · intento {mensaje.Intentos}, se reintenta a las {mensaje.ProximoIntento:HH:mm:ss} UTC"), CancellationToken.None);
        }
        // Aunque se esté cerrando la app, el resultado se guarda (si no, quedaría bloqueado hasta que caduque)
        await cola.Guardar(mensaje, CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fallo en {Entidad} (intento {Intento}, definitivo: {Definitivo})")]
    private static partial void LogFallo(ILogger log, string entidad, int intento, bool definitivo, Exception error);
}

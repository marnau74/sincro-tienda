using Microsoft.Extensions.Options;
using Sincro.Aplicacion.Cola;
using Sincro.Aplicacion.Deteccion;

namespace Sincro.Api;

public sealed class OpcionesSincronizacion
{
    public const string Seccion = "Sincronizacion";

    /// <summary>Si es false no se procesa la cola ni se buscan cambios (solo se reciben webhooks).</summary>
    public bool Activa { get; set; } = true;

    public int ProcesarColaCadaSegundos { get; set; } = 2;
    public int BuscarStockCadaMinutos { get; set; } = 5;
    public int RevisarPedidosCadaMinutos { get; set; } = 10;
    public int MensajesPorLote { get; set; } = 20;
}

public static class TareasPeriodicas
{
    public static IServiceCollection AddTareasPeriodicas(this IServiceCollection servicios, IConfiguration config)
    {
        var o = config.GetSection(OpcionesSincronizacion.Seccion).Get<OpcionesSincronizacion>() ?? new OpcionesSincronizacion();
        if (!o.Activa)
        {
            return servicios;
        }
        servicios.AddHostedService(sp => new TareaPeriodica("cola", TimeSpan.FromSeconds(o.ProcesarColaCadaSegundos), sp,
            async (s, ct) =>
            {
                // Mientras salgan lotes llenos se sigue sin esperar: así se vacía rápido una cola atrasada
                var procesador = s.GetRequiredService<ProcesadorCola>();
                int tomados, total = 0;
                do
                {
                    tomados = await procesador.ProcesarLote(o.MensajesPorLote, ct);
                    total += tomados;
                } while (tomados == o.MensajesPorLote && !ct.IsCancellationRequested);
                return total;
            }));
        servicios.AddHostedService(sp => new TareaPeriodica("stock", TimeSpan.FromMinutes(o.BuscarStockCadaMinutos), sp,
            (s, ct) => s.GetRequiredService<DetectarCambios>().Stock(ct)));
        servicios.AddHostedService(sp => new TareaPeriodica("pedidos", TimeSpan.FromMinutes(o.RevisarPedidosCadaMinutos), sp,
            (s, ct) => s.GetRequiredService<DetectarCambios>().Pedidos(ct)));
        return servicios;
    }
}

/// <summary>
/// Ejecuta un trabajo cada cierto tiempo en su propio ámbito de servicios. Un fallo se anota y no
/// para la tarea (p. ej. Odoo caído un rato: se vuelve a intentar en la siguiente pasada).
/// </summary>
public sealed partial class TareaPeriodica(
    string nombre, TimeSpan intervalo, IServiceProvider servicios, Func<IServiceProvider, CancellationToken, Task<int>> trabajo)
    : BackgroundService
{
    private readonly ILogger _log = servicios.GetRequiredService<ILoggerFactory>().CreateLogger($"Sincro.Tarea.{nombre}");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                await using var scope = servicios.CreateAsyncScope();
                var hechos = await trabajo(scope.ServiceProvider, stoppingToken);
                if (hechos > 0)
                {
                    LogHecho(_log, nombre, hechos);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                LogFallo(_log, nombre, e);
            }
        } while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tarea {Tarea}: {Hechos} elementos")]
    private static partial void LogHecho(ILogger log, string tarea, int hechos);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tarea {Tarea}: ha fallado esta pasada")]
    private static partial void LogFallo(ILogger log, string tarea, Exception error);
}

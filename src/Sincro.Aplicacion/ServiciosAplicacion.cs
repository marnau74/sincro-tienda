using Microsoft.Extensions.DependencyInjection;
using Sincro.Aplicacion.Cola;
using Sincro.Aplicacion.Deteccion;
using Sincro.Aplicacion.Pedidos;
using Sincro.Aplicacion.Stock;

namespace Sincro.Aplicacion;

public static class ServiciosAplicacion
{
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios)
    {
        servicios.AddScoped<ProcesarPedido>();
        servicios.AddScoped<ProcesarStock>();
        servicios.AddScoped<DetectarCambios>();
        servicios.AddScoped<ProcesadorCola>();
        servicios.AddSingleton(TimeProvider.System);
        return servicios;
    }
}

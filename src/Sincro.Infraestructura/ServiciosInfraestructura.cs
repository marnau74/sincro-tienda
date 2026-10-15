using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Sincro.Aplicacion;
using Sincro.Infraestructura.Odoo;
using Sincro.Infraestructura.Persistencia;
using Sincro.Infraestructura.WooCommerce;

namespace Sincro.Infraestructura;

public static class ServiciosInfraestructura
{
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios, IConfiguration config)
    {
        servicios.AddDbContext<SincroDbContext>(o => o
            .UseNpgsql(config.GetConnectionString("sincro") ?? throw new InvalidOperationException("Falta ConnectionStrings:sincro"))
            .UseSnakeCaseNamingConvention());
        servicios.AddScoped<IColaMensajes, ColaMensajesPg>();
        servicios.AddScoped<IRegistroCambios, RegistroCambiosPg>();
        servicios.AddScoped<ICursores, CursoresPg>();
        servicios.AddScoped<IPedidosSincronizados, PedidosSincronizadosPg>();
        servicios.AddScoped<ConsultasAdministracion>();

        servicios.Configure<WooCommerceOpciones>(config.GetSection(WooCommerceOpciones.Seccion));
        servicios.Configure<OdooOpciones>(config.GetSection(OdooOpciones.Seccion));

        // Reintentos rápidos solo en peticiones seguras de repetir (GET). Crear o confirmar un pedido
        // no se repite aquí: si la respuesta se pierde, el siguiente intento de la cola mira antes qué hay.
        servicios.AddTransient<AutenticacionWoo>();
        servicios.AddHttpClient<ITienda, WooCommerceTienda>((sp, http) =>
            {
                var url = sp.GetRequiredService<IOptions<WooCommerceOpciones>>().Value.Url;
                http.BaseAddress = new Uri(url.ToString().TrimEnd('/') + "/");
                http.DefaultRequestHeaders.UserAgent.ParseAdd("sincro-tienda/1.0");
            })
            .AddHttpMessageHandler<AutenticacionWoo>()
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());

        servicios.AddHttpClient<ClienteJson2>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<OdooOpciones>>().Value;
                http.BaseAddress = new Uri(o.Url.ToString().TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Authorization = new("bearer", o.ClaveApi);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("sincro-tienda/1.0");
                if (!string.IsNullOrEmpty(o.BaseDeDatos))
                {
                    http.DefaultRequestHeaders.Add("X-Odoo-Database", o.BaseDeDatos);
                }
            })
            .AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods()); // JSON-2 es todo POST: sin reintentos aquí
        servicios.AddScoped<IErp, OdooErp>();
        return servicios;
    }
}

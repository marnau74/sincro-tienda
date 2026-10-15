using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shouldly;

using Sincro.Api;

namespace Sincro.Integracion.Tests;

public class TareasPeriodicasTests
{
    private static IServiceCollection Registrar(bool activa)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sincronizacion:Activa"] = activa.ToString() })
            .Build();
        return new ServiceCollection().AddTareasPeriodicas(config);
    }

    [Fact]
    public void Se_registran_las_tres_tareas() =>
        // Con AddHostedService solo quedaba la primera: descarta los repetidos del mismo tipo
        Registrar(activa: true).Count(d => d.ServiceType == typeof(IHostedService)).ShouldBe(3);

    [Fact]
    public void Desactivada_no_se_registra_ninguna() =>
        Registrar(activa: false).ShouldNotContain(d => d.ServiceType == typeof(IHostedService));
}

using NetArchTest.Rules;

using Shouldly;

namespace Sincro.Arquitectura.Tests;

/// <summary>
/// Las dependencias van hacia dentro: el dominio no sabe de nadie, la aplicación solo del dominio
/// (habla con WooCommerce, Odoo y la base de datos a través de sus puertos) y nada conoce la API.
/// </summary>
public class CapasTests
{
    private static readonly System.Reflection.Assembly Dominio = typeof(Dominio.Pedidos.PedidoTienda).Assembly;
    private static readonly System.Reflection.Assembly Aplicacion = typeof(Aplicacion.IErp).Assembly;
    private static readonly System.Reflection.Assembly Infraestructura = typeof(Infraestructura.ServiciosInfraestructura).Assembly;

    [Fact]
    public void El_dominio_no_depende_de_otras_capas_ni_de_frameworks() =>
        Types.InAssembly(Dominio).ShouldNot()
            .HaveDependencyOnAny("Sincro.Aplicacion", "Sincro.Infraestructura", "Sincro.Api", "Microsoft.EntityFrameworkCore",
                "Microsoft.Extensions", "System.Net.Http")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void La_aplicacion_no_conoce_la_infraestructura() =>
        Types.InAssembly(Aplicacion).ShouldNot()
            .HaveDependencyOnAny("Sincro.Infraestructura", "Sincro.Api", "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void La_infraestructura_no_conoce_la_api() =>
        Types.InAssembly(Infraestructura).ShouldNot().HaveDependencyOn("Sincro.Api")
            .GetResult().IsSuccessful.ShouldBeTrue();
}

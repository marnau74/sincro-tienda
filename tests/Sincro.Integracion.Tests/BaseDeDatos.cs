using Microsoft.EntityFrameworkCore;
using Sincro.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Sincro.Integracion.Tests.BaseDeDatos))]

namespace Sincro.Integracion.Tests;

/// <summary>Un PostgreSQL real en Docker para todos los tests, con las migraciones aplicadas.</summary>
public sealed class BaseDeDatos : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string Cadena => _contenedor.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _contenedor.StartAsync();
        await using var db = Contexto();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _contenedor.DisposeAsync();

    public SincroDbContext Contexto() => new(new DbContextOptionsBuilder<SincroDbContext>()
        .UseNpgsql(Cadena)
        .UseSnakeCaseNamingConvention()
        .Options);

    public async Task Vaciar()
    {
        await using var db = Contexto();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE mensajes, registro, cursores, pedidos_sincronizados");
    }
}

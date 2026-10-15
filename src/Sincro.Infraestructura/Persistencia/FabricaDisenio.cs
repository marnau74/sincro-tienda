using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sincro.Infraestructura.Persistencia;

/// <summary>Solo para <c>dotnet ef</c> (generar migraciones): no se conecta a ninguna base de datos.</summary>
public sealed class FabricaDisenio : IDesignTimeDbContextFactory<SincroDbContext>
{
    public SincroDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SincroDbContext>()
        .UseNpgsql("Host=localhost;Database=sincro")
        .UseSnakeCaseNamingConvention()
        .Options);
}

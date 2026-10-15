using Microsoft.EntityFrameworkCore;
using Sincro.Dominio.Cola;
using Sincro.Dominio.Registro;

namespace Sincro.Infraestructura.Persistencia;

public sealed class SincroDbContext(DbContextOptions<SincroDbContext> opciones) : DbContext(opciones)
{
    public DbSet<Mensaje> Mensajes => Set<Mensaje>();
    public DbSet<RegistroCambio> Registro => Set<RegistroCambio>();
    public DbSet<Cursor> Cursores => Set<Cursor>();
    public DbSet<PedidoSincronizado> PedidosSincronizados => Set<PedidoSincronizado>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mensaje>(m =>
        {
            m.ToTable("mensajes");
            m.HasKey(x => x.Id);
            m.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            m.Property(x => x.ClaveIdempotencia).HasMaxLength(300);
            m.Property(x => x.ClaveOrden).HasMaxLength(200);
            m.Property(x => x.Referencia).HasMaxLength(150);
            m.Property(x => x.UltimoError).HasMaxLength(2000);
            // El mismo cambio no se guarda dos veces...
            m.HasIndex(x => x.ClaveIdempotencia).IsUnique();
            // ...y de cada entidad solo puede haber un mensaje esperando: el que espera leerá los datos al día
            m.HasIndex(x => x.ClaveOrden).IsUnique().HasFilter("estado = 'Pendiente'").HasDatabaseName("ix_mensajes_un_pendiente_por_entidad");
            m.HasIndex(x => new { x.Estado, x.ProximoIntento });
        });

        modelBuilder.Entity<RegistroCambio>(r =>
        {
            r.ToTable("registro");
            r.HasKey(x => x.Id);
            r.Property(x => x.Entidad).HasMaxLength(20);
            r.Property(x => x.Referencia).HasMaxLength(150);
            r.Property(x => x.Accion).HasMaxLength(50);
            r.Property(x => x.Detalle).HasMaxLength(4000);
            r.HasIndex(x => new { x.Entidad, x.Referencia });
            r.HasIndex(x => x.Fecha);
        });

        modelBuilder.Entity<Cursor>(c =>
        {
            c.ToTable("cursores");
            c.HasKey(x => x.Nombre);
            c.Property(x => x.Nombre).HasMaxLength(50);
        });

        modelBuilder.Entity<PedidoSincronizado>(p =>
        {
            p.ToTable("pedidos_sincronizados");
            p.HasKey(x => x.IdTienda);
            p.Property(x => x.IdTienda).ValueGeneratedNever();
            p.Property(x => x.UltimaAccion).HasMaxLength(50);
        });
    }
}

/// <summary>Hasta dónde se ha leído una fuente.</summary>
public sealed class Cursor
{
    public string Nombre { get; set; } = string.Empty;
    public DateTimeOffset Valor { get; set; }
}

/// <summary>Pedido de la tienda y su pedido en el ERP.</summary>
public sealed class PedidoSincronizado
{
    public long IdTienda { get; set; }
    public long IdErp { get; set; }
    public string UltimaAccion { get; set; } = string.Empty;
    public DateTimeOffset ActualizadoEn { get; set; }
}

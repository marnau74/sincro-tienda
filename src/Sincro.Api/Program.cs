using Microsoft.EntityFrameworkCore;

using Sincro.Api;
using Sincro.Aplicacion;
using Sincro.Infraestructura;
using Sincro.Infraestructura.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<SincroDbContext>("postgresql");
builder.Services.Configure<OpcionesSincronizacion>(builder.Configuration.GetSection(OpcionesSincronizacion.Seccion));
builder.Services.Configure<OpcionesAdministracion>(builder.Configuration.GetSection(OpcionesAdministracion.Seccion));
builder.Services.AddTareasPeriodicas(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue("BaseDeDatos:MigrarAlArrancar", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<SincroDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapWebhooks();
app.MapAdministracion();

await app.RunAsync();

/// <summary>Visible para los tests de la API (WebApplicationFactory).</summary>
public partial class Program;

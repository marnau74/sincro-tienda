using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Sincro.Dominio.Cola;
using Sincro.Infraestructura.Persistencia;

namespace Sincro.Integracion.Tests;

// Comparten la base de datos: no se ejecutan en paralelo entre sí
[Collection("bd")]
public sealed class ColaMensajesPgTests(BaseDeDatos bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _reloj = new(T0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(bd.Vaciar());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private ColaMensajesPg Cola(SincroDbContext db) => new(db, _reloj);

    private static Mensaje Pedido(long id, string version, int segundos = 0) =>
        Mensaje.Nuevo(TipoMensaje.Pedido, id.ToString(System.Globalization.CultureInfo.InvariantCulture), version, T0.AddSeconds(segundos));

    [Fact]
    public async Task El_mismo_cambio_no_se_encola_dos_veces()
    {
        await using var db = bd.Contexto();
        (await Cola(db).Encolar(Pedido(1, "v1"), Ct)).ShouldBeTrue();
        (await Cola(db).Encolar(Pedido(1, "v1"), Ct)).ShouldBeFalse();
        (await db.Mensajes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Si_ya_hay_uno_pendiente_de_la_entidad_no_se_encola_otro()
    {
        await using var db = bd.Contexto();
        (await Cola(db).Encolar(Pedido(1, "v1"), Ct)).ShouldBeTrue();
        (await Cola(db).Encolar(Pedido(1, "v2"), Ct)).ShouldBeFalse(); // el pendiente leerá la v2 al procesarse
        (await Cola(db).Encolar(Pedido(2, "v1"), Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Tomar_bloquea_y_solo_entrega_lo_que_ya_toca()
    {
        await using var db = bd.Contexto();
        var cola = Cola(db);
        await cola.Encolar(Pedido(1, "v1"), Ct);
        var futuro = Pedido(2, "v1");
        futuro.RegistrarFallo("caído", false, T0, PoliticaReintentos.PorDefecto, 0.5); // vuelve en 30 s
        await cola.Encolar(futuro, Ct);

        var lote = await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct);
        lote.Select(m => m.Referencia).ShouldBe(["1"]);
        lote[0].Estado.ShouldBe(EstadoMensaje.Procesando);
        lote[0].BloqueadoHasta.ShouldBe(T0.AddMinutes(5));
        (await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).ShouldBeEmpty(); // ya está tomado

        _reloj.Advance(TimeSpan.FromSeconds(40));
        (await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).Select(m => m.Referencia).ShouldBe(["2"]);
    }

    [Fact]
    public async Task Si_el_proceso_muere_otro_lo_retoma_al_vencer_el_plazo()
    {
        await using (var db = bd.Contexto())
        {
            await Cola(db).Encolar(Pedido(1, "v1"), Ct);
            (await Cola(db).TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).Count.ShouldBe(1);
        }
        await using var otro = bd.Contexto();
        (await Cola(otro).TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).ShouldBeEmpty();
        _reloj.Advance(TimeSpan.FromMinutes(6));
        (await Cola(otro).TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Un_cambio_nuevo_espera_a_que_termine_el_que_se_esta_procesando()
    {
        await using var db = bd.Contexto();
        var cola = Cola(db);
        await cola.Encolar(Pedido(1, "v1"), Ct);
        var primero = (await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).Single();

        // Mientras se procesa llega otro cambio del mismo pedido: se guarda, pero no se entrega aún
        (await cola.Encolar(Pedido(1, "v2", segundos: 1), Ct)).ShouldBeTrue();
        _reloj.Advance(TimeSpan.FromSeconds(2));
        (await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).ShouldBeEmpty();

        primero.MarcarHecho(_reloj.GetUtcNow());
        await cola.Guardar(primero, Ct);
        (await cola.TomarSiguientes(10, TimeSpan.FromMinutes(5), Ct)).Single().ClaveIdempotencia.ShouldEndWith("v2");
    }

    [Fact]
    public async Task Dos_procesos_a_la_vez_nunca_toman_el_mismo_mensaje()
    {
        await using (var db = bd.Contexto())
        {
            for (var i = 1; i <= 40; i++)
            {
                await Cola(db).Encolar(Pedido(i, "v1"), Ct);
            }
        }
        async Task<List<string>> Procesar()
        {
            await using var db = bd.Contexto();
            var tomados = new List<string>();
            while ((await Cola(db).TomarSiguientes(3, TimeSpan.FromMinutes(5), Ct)) is { Count: > 0 } lote)
            {
                tomados.AddRange(lote.Select(m => m.Referencia));
            }
            return tomados;
        }
        var resultados = await Task.WhenAll(Procesar(), Procesar(), Procesar());
        var todos = resultados.SelectMany(r => r).ToList();
        todos.Count.ShouldBe(40);
        todos.Distinct().Count().ShouldBe(40);
    }

    [Fact]
    public async Task Guardar_un_fallo_lo_deja_con_su_error_y_su_espera()
    {
        await using (var db = bd.Contexto())
        {
            await Cola(db).Encolar(Pedido(1, "v1"), Ct);
            var m = (await Cola(db).TomarSiguientes(1, TimeSpan.FromMinutes(5), Ct)).Single();
            m.RegistrarFallo("Odoo respondió 503", false, T0, PoliticaReintentos.PorDefecto, 0.5);
            await Cola(db).Guardar(m, Ct);
        }
        await using var leer = bd.Contexto();
        var guardado = await leer.Mensajes.SingleAsync(Ct);
        guardado.Estado.ShouldBe(EstadoMensaje.Pendiente);
        guardado.Intentos.ShouldBe(1);
        guardado.UltimoError.ShouldBe("Odoo respondió 503");
        guardado.ProximoIntento.ShouldBe(T0.AddSeconds(30));
    }
}

[Collection("bd")]
public sealed class RepositoriosPgTests(BaseDeDatos bd) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(bd.Vaciar());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Un_cursor_nunca_retrocede()
    {
        await using var db = bd.Contexto();
        var cursores = new CursoresPg(db);
        var t = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        await cursores.Guardar("x", t, Ct);
        await cursores.Guardar("x", t.AddHours(-1), Ct);
        (await cursores.Leer("x", Ct)).ShouldBe(t);
        (await cursores.Leer("otro", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task El_pedido_sincronizado_se_actualiza_al_repetirlo()
    {
        await using var db = bd.Contexto();
        var pedidos = new PedidosSincronizadosPg(db);
        await pedidos.Guardar(1042, 7, "crear_presupuesto", DateTimeOffset.UtcNow, Ct);
        await pedidos.Guardar(1042, 7, "confirmar", DateTimeOffset.UtcNow, Ct);
        (await pedidos.IdEnErp(1042, Ct)).ShouldBe(7);
        (await db.PedidosSincronizados.SingleAsync(Ct)).UltimaAccion.ShouldBe("confirmar");
    }
}

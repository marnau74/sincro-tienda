using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Sincro.Aplicacion.Cola;
using Sincro.Aplicacion.Deteccion;
using Sincro.Aplicacion.Pedidos;
using Sincro.Aplicacion.Stock;
using Sincro.Dominio;
using Sincro.Dominio.Cola;
using Sincro.Dominio.Pedidos;

using static Sincro.Unitarios.Tests.Contexto;

namespace Sincro.Unitarios.Tests;

public class ProcesarPedidoTests
{
    private readonly TiendaFalsa _tienda = new();
    private readonly ErpFalso _erp = new();
    private readonly PedidosSincronizadosFalsos _sincronizados = new();
    private readonly RegistroFalso _registro = new();
    private readonly ProcesarPedido _caso;

    public ProcesarPedidoTests() =>
        _caso = new ProcesarPedido(_tienda, _erp, _sincronizados, _registro, new FakeTimeProvider(), NullLogger<ProcesarPedido>.Instance);

    [Fact]
    public async Task Un_pedido_pagado_nuevo_se_crea_y_se_confirma()
    {
        _tienda.Pedidos[1042] = Ejemplos.Pedido();
        (await _caso.Ejecutar(1042, Ct)).ShouldBe(AccionErp.CrearYConfirmar);
        _erp.Llamadas.ShouldBe(["crear WC-1042", "confirmar 100"]);
        _erp.Pedidos["WC-1042"].Estado.ShouldBe(EstadoPedidoErp.Confirmado);
        _sincronizados.Filas[1042].ShouldBe((100L, "crear_y_confirmar"));
        _registro.Cambios.Single().Accion.ShouldBe("crear_y_confirmar");
    }

    [Fact]
    public async Task Procesarlo_otra_vez_no_duplica_nada()
    {
        _tienda.Pedidos[1042] = Ejemplos.Pedido();
        await _caso.Ejecutar(1042, Ct);
        (await _caso.Ejecutar(1042, Ct)).ShouldBe(AccionErp.Ninguna);
        _erp.Llamadas.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Si_se_cayo_tras_crearlo_el_siguiente_intento_solo_confirma()
    {
        _tienda.Pedidos[1042] = Ejemplos.Pedido();
        _erp.Pedidos["WC-1042"] = (77, EstadoPedidoErp.Presupuesto);
        (await _caso.Ejecutar(1042, Ct)).ShouldBe(AccionErp.Confirmar);
        _erp.Llamadas.ShouldBe(["confirmar 77"]);
    }

    [Fact]
    public async Task Un_pedido_cancelado_en_la_tienda_se_cancela_en_el_erp()
    {
        _tienda.Pedidos[1042] = Ejemplos.Pedido(estado: EstadoPedidoTienda.Cancelado);
        _erp.Pedidos["WC-1042"] = (77, EstadoPedidoErp.Confirmado);
        (await _caso.Ejecutar(1042, Ct)).ShouldBe(AccionErp.Cancelar);
        _erp.Pedidos["WC-1042"].Estado.ShouldBe(EstadoPedidoErp.Cancelado);
    }

    [Fact]
    public async Task Un_pedido_sin_sku_es_un_error_permanente()
    {
        _tienda.Pedidos[1042] = Ejemplos.Pedido(sku: "");
        var error = await Should.ThrowAsync<ErrorSincronizacion>(() => _caso.Ejecutar(1042, Ct));
        error.Permanente.ShouldBeTrue();
        _erp.Llamadas.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_pedido_que_ya_no_existe_se_anota_y_no_se_toca_el_erp()
    {
        (await _caso.Ejecutar(5, Ct)).ShouldBe(AccionErp.Ninguna);
        _registro.Cambios.Single().Accion.ShouldBe("no_existe");
        _erp.Llamadas.ShouldBeEmpty();
    }
}

public class ProcesarStockTests
{
    private readonly TiendaFalsa _tienda = new();
    private readonly ErpFalso _erp = new();
    private readonly RegistroFalso _registro = new();

    private ProcesarStock Caso() => new(_tienda, _erp, _registro, new FakeTimeProvider());

    [Fact]
    public async Task Pone_en_la_tienda_las_unidades_enteras_del_erp()
    {
        _erp.Stock["CAF-250"] = 12.7m;
        _tienda.Productos["CAF-250"] = 55;
        (await Caso().Ejecutar("CAF-250", Ct)).ShouldBe("stock_actualizado");
        _tienda.Stock[55].ShouldBe(12);
    }

    [Fact]
    public async Task Nunca_pone_stock_negativo()
    {
        _erp.Stock["CAF-250"] = -3;
        _tienda.Productos["CAF-250"] = 55;
        await Caso().Ejecutar("CAF-250", Ct);
        _tienda.Stock[55].ShouldBe(0);
    }

    [Fact]
    public async Task Un_producto_que_no_se_vende_online_se_anota_sin_error()
    {
        _erp.Stock["SOLO-ALMACEN"] = 4;
        (await Caso().Ejecutar("SOLO-ALMACEN", Ct)).ShouldBe("no_se_vende_online");
        _tienda.Stock.ShouldBeEmpty();
    }
}

public class DetectarCambiosTests
{
    private readonly TiendaFalsa _tienda = new();
    private readonly ErpFalso _erp = new();
    private readonly ColaFalsa _cola = new();
    private readonly CursoresFalsos _cursores = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private DetectarCambios Caso() => new(_tienda, _erp, _cola, _cursores, _reloj);

    [Fact]
    public async Task Stock_encola_cada_sku_una_vez_y_avanza_el_cursor_con_solape()
    {
        var cursor = new DateTimeOffset(2026, 10, 15, 11, 0, 0, TimeSpan.Zero);
        var ultimo = cursor.AddMinutes(30);
        _cursores.Valores[DetectarCambios.CursorStock] = cursor;
        _erp.SkusCambiados.AddRange(["A", "B", "A"]);
        _erp.UltimoCambio = ultimo;

        (await Caso().Stock(Ct)).ShouldBe(2);
        _erp.ConsultadoDesde.ShouldBe(cursor - DetectarCambios.Solape);
        _cursores.Valores[DetectarCambios.CursorStock].ShouldBe(ultimo);

        // La siguiente pasada ve otra vez el mismo cambio: no se encola dos veces
        (await Caso().Stock(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Pedidos_recorre_todas_las_paginas_y_guarda_el_mas_reciente()
    {
        var baseHora = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        for (var i = 1; i <= 5; i++)
        {
            _tienda.Pedidos[i] = Ejemplos.Pedido(i, modificado: baseHora.AddMinutes(i));
        }
        (await Caso().Pedidos(Ct, porPagina: 2)).ShouldBe(5);
        _cola.Mensajes.Select(m => m.Referencia).ShouldBe(["1", "2", "3", "4", "5"]);
        _cursores.Valores[DetectarCambios.CursorPedidos].ShouldBe(baseHora.AddMinutes(5));
    }
}

public class ProcesadorColaTests
{
    private readonly TiendaFalsa _tienda = new();
    private readonly ErpFalso _erp = new();
    private readonly ColaFalsa _cola = new();
    private readonly RegistroFalso _registro = new();
    private readonly FakeTimeProvider _reloj = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    private ProcesadorCola Procesador() => new(
        _cola,
        new ProcesarPedido(_tienda, _erp, new PedidosSincronizadosFalsos(), _registro, _reloj, NullLogger<ProcesarPedido>.Instance),
        new ProcesarStock(_tienda, _erp, _registro, _reloj),
        _registro,
        _reloj,
        NullLogger<ProcesadorCola>.Instance);

    [Fact]
    public async Task Un_mensaje_bien_procesado_queda_hecho()
    {
        _tienda.Pedidos[1] = Ejemplos.Pedido(1);
        await _cola.Encolar(EncolarPedido.Mensaje(1, _reloj.GetUtcNow(), _reloj.GetUtcNow()), Ct);
        (await Procesador().ProcesarLote(10, Ct)).ShouldBe(1);
        _cola.Mensajes.Single().Estado.ShouldBe(EstadoMensaje.Hecho);
    }

    [Fact]
    public async Task Un_error_de_red_se_reintenta_y_uno_permanente_queda_fallido()
    {
        _tienda.Pedidos[1] = Ejemplos.Pedido(1);
        _tienda.Pedidos[2] = Ejemplos.Pedido(2, sku: "");
        _erp.FallarAlCrear = new HttpRequestException("ERP caído");
        await _cola.Encolar(EncolarPedido.Mensaje(1, _reloj.GetUtcNow(), _reloj.GetUtcNow()), Ct);
        await _cola.Encolar(EncolarPedido.Mensaje(2, _reloj.GetUtcNow(), _reloj.GetUtcNow()), Ct);

        await Procesador().ProcesarLote(10, Ct);

        var red = _cola.Mensajes.Single(m => m.Referencia == "1");
        red.Estado.ShouldBe(EstadoMensaje.Pendiente);
        red.ProximoIntento.ShouldBeGreaterThan(_reloj.GetUtcNow());
        var permanente = _cola.Mensajes.Single(m => m.Referencia == "2");
        permanente.Estado.ShouldBe(EstadoMensaje.Fallido);
        permanente.UltimoError!.ShouldContain("SKU");
        _registro.Cambios.Select(c => c.Accion).ShouldBe(["error_reintentara", "fallido"], ignoreOrder: true);
    }
}

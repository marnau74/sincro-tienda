using System.Text.Json;

using Microsoft.Extensions.Options;

using Shouldly;

using Sincro.Dominio;
using Sincro.Dominio.Pedidos;
using Sincro.Infraestructura.Odoo;

using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Sincro.Integracion.Tests;

public sealed class OdooErpTests : IDisposable
{
    private readonly WireMockServer _odoo = WireMockServer.Start();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _odoo.Stop();

    private OdooErp Erp()
    {
        var http = new HttpClient { BaseAddress = new Uri(_odoo.Url! + "/") };
        http.DefaultRequestHeaders.Authorization = new("bearer", "clave-de-prueba");
        http.DefaultRequestHeaders.Add("X-Odoo-Database", "tienda");
        return new OdooErp(new ClienteJson2(http), Options.Create(new OdooOpciones()));
    }

    private void Responder(string ruta, string json, string? cuerpoContiene = null)
    {
        var peticion = Request.Create().WithPath($"/json/2/{ruta}").UsingPost();
        if (cuerpoContiene is not null)
        {
            peticion = peticion.WithBody(new WildcardMatcher($"*{cuerpoContiene}*"));
        }
        _odoo.Given(peticion).RespondWith(Response.Create().WithBody(json).WithHeader("Content-Type", "application/json"));
    }

    private JsonElement Cuerpo(string ruta) =>
        JsonDocument.Parse(_odoo.LogEntries.Last(e => e.RequestMessage!.Path == $"/json/2/{ruta}").RequestMessage!.Body!).RootElement;

    [Theory]
    [InlineData("draft", EstadoPedidoErp.Presupuesto)]
    [InlineData("sale", EstadoPedidoErp.Confirmado)]
    [InlineData("cancel", EstadoPedidoErp.Cancelado)]
    public async Task Busca_el_pedido_por_la_referencia_de_la_tienda(string estado, EstadoPedidoErp esperado)
    {
        Responder("sale.order/search_read", $$"""[{"id":7,"state":"{{estado}}"}]""");

        (await Erp().BuscarPedido("WC-1042", Ct)).ShouldBe((7L, esperado));

        var peticion = _odoo.LogEntries.Single().RequestMessage!;
        peticion.Headers!["Authorization"].Single().ShouldBe("bearer clave-de-prueba");
        peticion.Headers["X-Odoo-Database"].Single().ShouldBe("tienda");
        // Odoo ignora los cuerpos por trozos: tiene que ir con su longitud
        peticion.Headers.ShouldContainKey("Content-Length");
        peticion.Headers.ContainsKey("Transfer-Encoding").ShouldBeFalse();
        Cuerpo("sale.order/search_read").GetProperty("domain").GetRawText().ShouldBe("""[["client_order_ref","=","WC-1042"]]""");
    }

    [Fact]
    public async Task Si_no_esta_devuelve_null()
    {
        Responder("sale.order/search_read", "[]");
        (await Erp().BuscarPedido("WC-1", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Crea_el_cliente_si_no_existe_y_el_pedido_con_sus_lineas_y_el_envio()
    {
        Responder("res.partner/search_read", "[]");
        Responder("res.country/search_read", """[{"id":68}]""");
        Responder("res.partner/create", "[31]");
        Responder("product.product/search_read", """[{"id":5,"default_code":"CAF-250"},{"id":9,"default_code":"ENVIO"}]""");
        Responder("sale.order/create", "[88]");
        var pedido = new PedidoTienda(1042, "1042", EstadoPedidoTienda.Procesando, DateTimeOffset.UtcNow, "EUR",
            new ClienteTienda("ana@ejemplo.es", "Ana Pérez", null, null, new DireccionPostal("Calle Mayor 1", "Murcia", "30001", "MU", "ES")),
            [new LineaPedido("CAF-250", "Café", 3, 3.15m)], 4.95m, "Dejar en portería");

        (await Erp().CrearPedido(pedido, Ct)).ShouldBe(88);

        Cuerpo("res.partner/create").GetProperty("vals_list")[0].GetProperty("country_id").GetInt64().ShouldBe(68);
        var orden = Cuerpo("sale.order/create").GetProperty("vals_list")[0];
        orden.GetProperty("partner_id").GetInt64().ShouldBe(31);
        orden.GetProperty("client_order_ref").GetString().ShouldBe("WC-1042");
        orden.GetProperty("order_line").GetRawText().ShouldBe(
            """[[0,0,{"product_id":5,"product_uom_qty":3,"price_unit":3.15}],[0,0,{"product_id":9,"product_uom_qty":1,"price_unit":4.95}]]""");
    }

    [Fact]
    public async Task Un_sku_que_no_existe_en_odoo_es_permanente_y_no_crea_nada()
    {
        Responder("res.partner/search_read", """[{"id":31}]""");
        Responder("product.product/search_read", "[]");
        var pedido = new PedidoTienda(1, "1", EstadoPedidoTienda.Procesando, DateTimeOffset.UtcNow, "EUR",
            new ClienteTienda("a@b.es", "A", null, null, new DireccionPostal(null, null, null, null, null)),
            [new LineaPedido("NO-EXISTE", "X", 1, 1)], 0, null);

        var error = await Should.ThrowAsync<ErrorSincronizacion>(() => Erp().CrearPedido(pedido, Ct));

        error.Permanente.ShouldBeTrue();
        error.Message.ShouldContain("NO-EXISTE");
        _odoo.LogEntries.ShouldNotContain(e => e.RequestMessage!.Path == "/json/2/sale.order/create");
    }

    [Fact]
    public async Task Clave_rechazada_es_permanente_y_un_503_es_pasajero()
    {
        _odoo.Given(Request.Create().WithPath("/json/2/sale.order/search_read").UsingPost().WithBody(new WildcardMatcher("*WC-1\"*")))
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"name":"werkzeug.exceptions.Unauthorized","message":"Invalid apikey"}"""));
        _odoo.Given(Request.Create().WithPath("/json/2/sale.order/search_read").UsingPost().WithBody(new WildcardMatcher("*WC-2\"*")))
            .RespondWith(Response.Create().WithStatusCode(503));

        var permanente = await Should.ThrowAsync<ErrorSincronizacion>(() => Erp().BuscarPedido("WC-1", Ct));
        permanente.Permanente.ShouldBeTrue();
        permanente.Message.ShouldContain("Invalid apikey");
        await Should.ThrowAsync<HttpRequestException>(() => Erp().BuscarPedido("WC-2", Ct));
    }

    [Fact]
    public async Task Cancela_sin_el_asistente_de_aviso()
    {
        Responder("sale.order/action_cancel", "true");
        await Erp().CancelarPedido(7, Ct);
        var cuerpo = Cuerpo("sale.order/action_cancel");
        cuerpo.GetProperty("ids").GetRawText().ShouldBe("[7]");
        cuerpo.GetProperty("context").GetProperty("disable_cancel_warning").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Detecta_los_skus_con_movimientos_y_la_fecha_del_ultimo()
    {
        Responder("stock.move/search_read", """
            [{"id":1,"product_id":[5,"Café"],"date":"2026-10-15 09:00:00"},
             {"id":2,"product_id":[6,"Taza"],"date":"2026-10-15 09:05:00"},
             {"id":3,"product_id":[5,"Café"],"date":"2026-10-15 09:10:00"}]
            """);
        Responder("product.product/read", """[{"id":5,"default_code":"CAF-250"},{"id":6,"default_code":false}]""");

        var (skus, ultimo) = await Erp().StockCambiadoDesde(new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.FromHours(2)), Ct);

        skus.ShouldBe(["CAF-250"]); // los productos sin referencia interna no se pueden emparejar con la tienda
        ultimo.ShouldBe(new DateTimeOffset(2026, 10, 15, 9, 10, 0, TimeSpan.Zero));
        var filtroFecha = Cuerpo("stock.move/search_read").GetProperty("domain")[1];
        filtroFecha.EnumerateArray().Select(x => x.GetString()).ShouldBe(["date", ">", "2026-10-15 08:00:00"]);
    }

    [Fact]
    public async Task Lee_el_stock_disponible_del_sku()
    {
        Responder("product.product/search_read", """[{"id":5,"free_qty":12.0}]""", cuerpoContiene: "CAF-250");
        Responder("product.product/search_read", "[]", cuerpoContiene: "NADA");
        (await Erp().LeerStock("CAF-250", Ct)).ShouldBe(12m);
        (await Erp().LeerStock("NADA", Ct)).ShouldBeNull();
    }
}

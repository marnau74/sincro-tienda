using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Sincro.Dominio;
using Sincro.Dominio.Pedidos;
using Sincro.Infraestructura.WooCommerce;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Sincro.Integracion.Tests;

public sealed class WooCommerceTiendaTests : IDisposable
{
    private readonly WireMockServer _woo = WireMockServer.Start();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _woo.Stop();

    private WooCommerceTienda Tienda()
    {
        var opciones = Options.Create(new WooCommerceOpciones
        {
            Url = new Uri(_woo.Url!),
            ClaveCliente = "ck_prueba",
            SecretoCliente = "cs_secreto",
        });
        var http = new HttpClient(new AutenticacionWoo(opciones, new FakeTimeProvider()) { InnerHandler = new HttpClientHandler() })
        {
            BaseAddress = new Uri(_woo.Url! + "/"),
        };
        return new WooCommerceTienda(http);
    }

    private const string PedidoJson = """
        {
          "id": 1042, "number": "1042", "status": "processing", "currency": "EUR",
          "date_modified_gmt": "2026-10-15T09:30:00",
          "shipping_total": "4.95", "customer_note": "Dejar en portería",
          "billing": { "first_name": "Ana", "last_name": "Pérez", "company": "", "address_1": "Calle Mayor 1", "address_2": "2ºB",
                       "city": "Murcia", "state": "MU", "postcode": "30001", "country": "ES", "email": "ana@ejemplo.es", "phone": "600000000" },
          "line_items": [
            { "name": "Café molido 250 g", "sku": "CAF-250", "quantity": 3, "subtotal": "10.50", "total": "9.45" },
            { "name": "Taza", "sku": "TAZA-01", "quantity": 1, "subtotal": "6.00", "total": "6.00" }
          ]
        }
        """;

    [Fact]
    public async Task Traduce_el_pedido_con_precios_sin_impuestos_y_con_descuento()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders/1042").UsingGet())
            .RespondWith(Response.Create().WithBody(PedidoJson).WithHeader("Content-Type", "application/json"));

        var p = (await Tienda().ObtenerPedido(1042, Ct))!;

        p.Estado.ShouldBe(EstadoPedidoTienda.Procesando);
        p.ModificadoEn.ShouldBe(new DateTimeOffset(2026, 10, 15, 9, 30, 0, TimeSpan.Zero));
        p.Cliente.Nombre.ShouldBe("Ana Pérez");
        p.Cliente.Empresa.ShouldBeNull();
        p.Cliente.Direccion.Calle.ShouldBe("Calle Mayor 1 2ºB");
        p.Lineas[0].ShouldBe(new LineaPedido("CAF-250", "Café molido 250 g", 3, 3.15m)); // 9,45 / 3 (con el descuento)
        p.GastosEnvio.ShouldBe(4.95m);
        p.Nota.ShouldBe("Dejar en portería");
        p.Validar().ShouldBeEmpty();
    }

    [Fact]
    public async Task Por_http_firma_con_oauth_y_sin_contrasenas_en_cabeceras()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders/1042").UsingGet())
            .RespondWith(Response.Create().WithBody(PedidoJson));

        await Tienda().ObtenerPedido(1042, Ct);

        var peticion = _woo.LogEntries.Single().RequestMessage!;
        peticion.Query!.Keys.ShouldBe(["oauth_consumer_key", "oauth_nonce", "oauth_signature_method", "oauth_timestamp", "oauth_signature"], ignoreOrder: true);
        peticion.Query["oauth_signature_method"].ToString().ShouldBe("HMAC-SHA256");
        peticion.Headers!.ContainsKey("Authorization").ShouldBeFalse();
    }

    [Fact]
    public void La_firma_coincide_con_la_que_calcula_woocommerce()
    {
        // Valor de referencia calculado aparte con el algoritmo de class-wc-rest-authentication.php
        var uri = AutenticacionWoo.Firmar(HttpMethod.Get,
            new Uri("http://tienda.local/wp-json/wc/v3/orders?per_page=50&modified_after=2026-10-15T09:30:00"),
            "ck_prueba", "cs_secreto", "abc123", "1792000000");
        var firma = System.Web.HttpUtility.ParseQueryString(uri.Query)["oauth_signature"];
        firma.ShouldBe("SHnDL7Oe+PADMnrSPUHgZ7JYn046Et5vVc6tCjz69NA=");
    }

    [Fact]
    public async Task Un_pedido_que_no_existe_es_null()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders/9").UsingGet()).RespondWith(Response.Create().WithStatusCode(404));
        (await Tienda().ObtenerPedido(9, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Credenciales_mal_puestas_es_un_error_permanente_y_un_500_es_pasajero()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders/1").UsingGet()).RespondWith(Response.Create().WithStatusCode(401));
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders/2").UsingGet()).RespondWith(Response.Create().WithStatusCode(500));

        (await Should.ThrowAsync<ErrorSincronizacion>(() => Tienda().ObtenerPedido(1, Ct))).Permanente.ShouldBeTrue();
        await Should.ThrowAsync<HttpRequestException>(() => Tienda().ObtenerPedido(2, Ct));
    }

    [Fact]
    public async Task Lista_los_pedidos_modificados_en_utc_y_por_paginas()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/orders").UsingGet())
            .RespondWith(Response.Create().WithBody("""[{"id":7,"date_modified_gmt":"2026-10-15T10:00:00"}]"""));

        var lista = await Tienda().PedidosModificadosDesde(new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.FromHours(2)), 3, 50, Ct);

        lista.ShouldBe([(7L, new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero))]);
        var q = _woo.LogEntries.Single().RequestMessage!.Query!;
        q["modified_after"].ToString().ShouldBe("2026-10-15T09:30:00");
        q["dates_are_gmt"].ToString().ShouldBe("true");
        q["page"].ToString().ShouldBe("3");
    }

    [Fact]
    public async Task Busca_por_sku_y_actualiza_el_stock()
    {
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/products").WithParam("sku", "CAF-250").UsingGet())
            .RespondWith(Response.Create().WithBody("""[{"id":55,"sku":"CAF-250"}]"""));
        _woo.Given(Request.Create().WithPath("/wp-json/wc/v3/products/55").UsingPut()).RespondWith(Response.Create().WithBody("{}"));

        var id = await Tienda().BuscarProductoPorSku("CAF-250", Ct);
        await Tienda().ActualizarStock(id!.Value, 12, Ct);

        id.ShouldBe(55);
        var put = _woo.LogEntries[_woo.LogEntries.Count - 1].RequestMessage!;
        put.Body.ShouldBe("""{"manage_stock":true,"stock_quantity":12}""");
    }
}

public class VerificadorWebhookTests
{
    private static readonly byte[] Cuerpo = Encoding.UTF8.GetBytes("""{"id":1042}""");

    [Fact]
    public void Acepta_la_firma_correcta_y_rechaza_las_demas()
    {
        var firma = VerificadorWebhook.Firmar(Cuerpo, "secreto");
        VerificadorWebhook.FirmaValida(Cuerpo, firma, "secreto").ShouldBeTrue();
        VerificadorWebhook.FirmaValida(Cuerpo, firma, "otro").ShouldBeFalse();
        VerificadorWebhook.FirmaValida(Encoding.UTF8.GetBytes("""{"id":1043}"""), firma, "secreto").ShouldBeFalse();
        VerificadorWebhook.FirmaValida(Cuerpo, "no es base64", "secreto").ShouldBeFalse();
        VerificadorWebhook.FirmaValida(Cuerpo, null, "secreto").ShouldBeFalse();
        VerificadorWebhook.FirmaValida(Cuerpo, firma, "").ShouldBeFalse(); // sin secreto configurado no se acepta nada
    }
}

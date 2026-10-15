using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Sincro.Dominio.Cola;
using Sincro.Infraestructura.WooCommerce;

namespace Sincro.Integracion.Tests;

[Collection("bd")]
public sealed class ApiTests(BaseDeDatos bd) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _app = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await bd.Vaciar();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(w => w
            .UseSetting("ConnectionStrings:sincro", bd.Cadena)
            .UseSetting("BaseDeDatos:MigrarAlArrancar", "false")
            .UseSetting("Sincronizacion:Activa", "false") // sin tareas en segundo plano: los tests miran la cola
            .UseSetting("WooCommerce:SecretoWebhook", "secreto")
            .UseSetting("Administracion:ClaveApi", "admin"));
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private static HttpRequestMessage Webhook(string json, string tema = "order.updated", string? secreto = "secreto")
    {
        var cuerpo = Encoding.UTF8.GetBytes(json);
        var m = new HttpRequestMessage(HttpMethod.Post, "/webhooks/woocommerce") { Content = new ByteArrayContent(cuerpo) };
        m.Content.Headers.ContentType = new("application/json");
        m.Headers.Add("X-WC-Webhook-Topic", tema);
        if (secreto is not null)
        {
            m.Headers.Add("X-WC-Webhook-Signature", VerificadorWebhook.Firmar(cuerpo, secreto));
        }
        return m;
    }

    [Fact]
    public async Task Un_webhook_firmado_deja_el_pedido_en_la_cola()
    {
        var http = _app.CreateClient();
        var r = await http.SendAsync(Webhook("""{"id":1042,"status":"processing","date_modified_gmt":"2026-10-15T09:30:00"}"""), Ct);

        r.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await using var db = bd.Contexto();
        var m = await db.Mensajes.SingleAsync(Ct);
        m.Tipo.ShouldBe(TipoMensaje.Pedido);
        m.Referencia.ShouldBe("1042");
        m.ClaveIdempotencia.ShouldBe("pedido:1042:2026-10-15T09:30:00.0000000+00:00");
    }

    [Fact]
    public async Task El_mismo_webhook_repetido_no_duplica()
    {
        var http = _app.CreateClient();
        const string json = """{"id":7,"date_modified_gmt":"2026-10-15T09:30:00"}""";
        await http.SendAsync(Webhook(json), Ct);
        var r = await http.SendAsync(Webhook(json), Ct);

        (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("encolado").GetBoolean().ShouldBeFalse();
        await using var db = bd.Contexto();
        (await db.Mensajes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Sin_firma_valida_se_rechaza()
    {
        var http = _app.CreateClient();
        (await http.SendAsync(Webhook("""{"id":1}""", secreto: "otro"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await http.SendAsync(Webhook("""{"id":1}""", secreto: null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await using var db = bd.Contexto();
        (await db.Mensajes.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task La_prueba_de_woocommerce_al_crear_el_webhook_recibe_un_200()
    {
        var http = _app.CreateClient();
        var r = await http.PostAsync("/webhooks/woocommerce",
            new StringContent("webhook_id=3", Encoding.UTF8, "application/x-www-form-urlencoded"), Ct);
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Los_temas_que_no_son_pedidos_se_ignoran()
    {
        var http = _app.CreateClient();
        var r = await http.SendAsync(Webhook("""{"id":55}""", tema: "product.updated"), Ct);
        r.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await using var db = bd.Contexto();
        (await db.Mensajes.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task La_administracion_pide_la_clave()
    {
        var http = _app.CreateClient();
        (await http.GetAsync("/api/resumen", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        http.DefaultRequestHeaders.Add("X-Api-Key", "admin");
        var resumen = await http.GetFromJsonAsync<JsonElement>("/api/resumen", Ct);
        resumen.GetProperty("mensajesPorEstado").GetProperty("Fallido").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Un_mensaje_fallido_se_ve_y_se_puede_reintentar()
    {
        var ahora = DateTimeOffset.UtcNow;
        var m = Mensaje.Nuevo(TipoMensaje.Pedido, "9", "v1", ahora);
        m.RegistrarFallo("No existen en Odoo los productos con referencia X", true, ahora, PoliticaReintentos.PorDefecto, 0.5);
        await using (var db = bd.Contexto())
        {
            db.Mensajes.Add(m);
            await db.SaveChangesAsync(Ct);
        }
        var http = _app.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", "admin");

        var fallidos = await http.GetFromJsonAsync<JsonElement>("/api/mensajes?estado=Fallido", Ct);
        fallidos.GetArrayLength().ShouldBe(1);
        fallidos[0].GetProperty("ultimoError").GetString()!.ShouldContain("Odoo");

        (await http.PostAsync($"/api/mensajes/{m.Id}/reintentar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await http.PostAsync($"/api/mensajes/{m.Id}/reintentar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await http.PostAsync($"/api/mensajes/{Guid.NewGuid()}/reintentar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task La_salud_comprueba_la_base_de_datos()
    {
        var r = await _app.CreateClient().GetAsync("/health", Ct);
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

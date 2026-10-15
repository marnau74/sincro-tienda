using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sincro.Aplicacion;
using Sincro.Aplicacion.Deteccion;
using Sincro.Infraestructura.WooCommerce;

namespace Sincro.Api;

public static class Webhooks
{
    private const int TamanoMaximo = 1024 * 1024;

    public static void MapWebhooks(this IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/woocommerce", RecibirWooCommerce)
            .WithSummary("Webhook de WooCommerce (pedido creado, actualizado o borrado)")
            .WithDescription("Comprueba la firma, guarda el aviso en la cola y responde enseguida; el pedido se lleva al ERP después.");

    /// <summary>
    /// Responde en milisegundos (WooCommerce desactiva los webhooks que fallan o tardan): solo se
    /// comprueba la firma y se apunta en la cola que el pedido ha cambiado.
    /// </summary>
    internal static async Task<IResult> RecibirWooCommerce(
        HttpRequest peticion, IOptions<WooCommerceOpciones> opciones, IColaMensajes cola, TimeProvider reloj, CancellationToken ct)
    {
        if (peticion.ContentLength > TamanoMaximo)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        using var memoria = new MemoryStream();
        await peticion.Body.CopyToAsync(memoria, ct);
        var cuerpo = memoria.ToArray();
        var firma = peticion.Headers["X-WC-Webhook-Signature"].ToString();

        // Al crear el webhook, WooCommerce manda una prueba sin firmar con «webhook_id=N»
        if (firma.Length == 0 && Encoding.UTF8.GetString(cuerpo).StartsWith("webhook_id=", StringComparison.Ordinal))
        {
            return Results.Ok();
        }
        if (!VerificadorWebhook.FirmaValida(cuerpo, firma, opciones.Value.SecretoWebhook))
        {
            return Results.Unauthorized();
        }

        var tema = peticion.Headers["X-WC-Webhook-Topic"].ToString();
        if (!tema.StartsWith("order.", StringComparison.Ordinal))
        {
            return Results.Accepted(value: new { encolado = false, motivo = $"tema {tema} no se sincroniza" });
        }
        long id;
        DateTimeOffset modificado;
        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            id = json.RootElement.GetProperty("id").GetInt64();
            modificado = json.RootElement.TryGetProperty("date_modified_gmt", out var f)
                && DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
                ? new DateTimeOffset(d, TimeSpan.Zero)
                : reloj.GetUtcNow(); // order.deleted solo trae el id
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Results.BadRequest(new { error = "El cuerpo no es un pedido de WooCommerce" });
        }

        var encolado = await cola.Encolar(EncolarPedido.Mensaje(id, modificado, reloj.GetUtcNow()), ct);
        return Results.Accepted(value: new { encolado });
    }
}

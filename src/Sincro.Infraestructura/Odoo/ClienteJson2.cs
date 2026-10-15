using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Sincro.Dominio;

namespace Sincro.Infraestructura.Odoo;

/// <summary>
/// API externa JSON-2 de Odoo 19: <c>POST /json/2/&lt;modelo&gt;/&lt;método&gt;</c> con los argumentos por
/// nombre y una clave de API en <c>Authorization: bearer</c>. Sustituye a XML-RPC/JSON-RPC, que
/// Odoo retira en la versión 22. Cada llamada es una transacción propia.
/// </summary>
public sealed class ClienteJson2(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<JsonElement> Llamar(string modelo, string metodo, object argumentos, CancellationToken ct)
    {
        // El cuerpo va entero y con Content-Length: Odoo ignora los cuerpos enviados por trozos (chunked), y
        // entonces una búsqueda llega sin filtro y devuelve registros que no son.
        using var cuerpo = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(argumentos, Json));
        cuerpo.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
        using var r = await http.PostAsync($"json/2/{modelo}/{metodo}", cuerpo, ct);
        if (r.IsSuccessStatusCode)
        {
            return await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        throw await Error(r, modelo, metodo, ct);
    }

    /// <summary>
    /// 401/403: clave o permisos (permanente). Otros 4xx: Odoo rechazó los datos (validación, registro
    /// que falta...), también permanente. 5xx y 429: pasajero, se reintenta.
    /// </summary>
    private static async Task<Exception> Error(HttpResponseMessage r, string modelo, string metodo, CancellationToken ct)
    {
        var cuerpo = await r.Content.ReadAsStringAsync(ct);
        var mensaje = cuerpo;
        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            if (json.RootElement.TryGetProperty("message", out var m))
            {
                mensaje = m.GetString() ?? cuerpo;
            }
        }
        catch (JsonException)
        {
            // No es el JSON de error de Odoo (p. ej. un proxy delante): se deja el texto tal cual
        }
        if (mensaje.Length > 300)
        {
            mensaje = mensaje[..300];
        }
        var codigo = (int)r.StatusCode;
        var texto = $"Odoo {modelo}.{metodo} respondió {codigo}: {mensaje}";
        return codigo is >= 400 and < 500 and not 408 and not 429
            ? new ErrorSincronizacion(texto, permanente: true)
            : new HttpRequestException(texto, null, (HttpStatusCode)codigo);
    }
}

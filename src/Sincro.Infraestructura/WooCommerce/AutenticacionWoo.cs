using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace Sincro.Infraestructura.WooCommerce;

/// <summary>
/// Autentica cada petición a la API REST de WooCommerce. Por HTTPS basta con la clave y el secreto
/// en Basic; por HTTP (la tienda local de la demo) WooCommerce exige firmar con OAuth 1.0a de una
/// sola pata, con HMAC-SHA256 (ver <c>class-wc-rest-authentication.php</c>).
/// </summary>
public sealed class AutenticacionWoo(IOptions<WooCommerceOpciones> opciones, TimeProvider reloj) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var o = opciones.Value;
        var uri = request.RequestUri!;
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            var credenciales = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClaveCliente}:{o.SecretoCliente}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credenciales);
        }
        else
        {
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var timestamp = reloj.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            request.RequestUri = Firmar(request.Method, uri, o.ClaveCliente, o.SecretoCliente, nonce, timestamp);
        }
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>Devuelve la URL con los parámetros <c>oauth_*</c> y la firma añadidos a la consulta.</summary>
    public static Uri Firmar(HttpMethod metodo, Uri uri, string clave, string secreto, string nonce, string timestamp)
    {
        var parametros = LeerConsulta(uri.Query);
        parametros["oauth_consumer_key"] = clave;
        parametros["oauth_nonce"] = nonce;
        parametros["oauth_signature_method"] = "HMAC-SHA256";
        parametros["oauth_timestamp"] = timestamp;

        // Ordenados por clave (strcmp, como WooCommerce); clave=valor codificados y la cadena entera otra vez
        var ordenados = parametros.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        var normalizados = string.Join("%26", ordenados.Select(p => Codificar($"{Codificar(p.Key)}={Codificar(p.Value)}")));
        var baseUrl = uri.GetLeftPart(UriPartial.Path);
        var cadena = $"{metodo.Method.ToUpperInvariant()}&{Codificar(baseUrl)}&{normalizados}";
        var firma = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto + "&"), Encoding.UTF8.GetBytes(cadena)));

        var consulta = string.Join("&", ordenados.Select(p => $"{Codificar(p.Key)}={Codificar(p.Value)}"))
            + "&oauth_signature=" + Codificar(firma);
        return new UriBuilder(uri) { Query = consulta }.Uri;
    }

    /// <summary>RFC 3986, igual que <c>rawurlencode</c> de PHP.</summary>
    private static string Codificar(string s) => Uri.EscapeDataString(s);

    private static Dictionary<string, string> LeerConsulta(string consulta)
    {
        var res = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var par in consulta.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = par.IndexOf('=', StringComparison.Ordinal);
            var k = Uri.UnescapeDataString(i < 0 ? par : par[..i]);
            res[k] = i < 0 ? string.Empty : Uri.UnescapeDataString(par[(i + 1)..].Replace('+', ' '));
        }
        return res;
    }
}

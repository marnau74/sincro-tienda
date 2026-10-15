using System.Security.Cryptography;
using System.Text;

namespace Sincro.Infraestructura.WooCommerce;

/// <summary>
/// Comprueba la cabecera <c>X-WC-Webhook-Signature</c>: HMAC-SHA256 del cuerpo exacto con el secreto
/// del webhook, en base64. La comparación es de tiempo constante para no dar pistas a quien pruebe firmas.
/// </summary>
public static class VerificadorWebhook
{
    public static bool FirmaValida(ReadOnlySpan<byte> cuerpo, string? firma, string secreto)
    {
        if (string.IsNullOrEmpty(firma) || string.IsNullOrEmpty(secreto))
        {
            return false;
        }
        Span<byte> recibida = stackalloc byte[64];
        if (!Convert.TryFromBase64String(firma, recibida, out var escritos))
        {
            return false;
        }
        var esperada = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), cuerpo);
        return CryptographicOperations.FixedTimeEquals(esperada, recibida[..escritos]);
    }

    public static string Firmar(ReadOnlySpan<byte> cuerpo, string secreto) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), cuerpo));
}

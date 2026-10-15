namespace Sincro.Infraestructura.WooCommerce;

public sealed class WooCommerceOpciones
{
    public const string Seccion = "WooCommerce";

    /// <summary>Dirección de la tienda (p. ej. <c>https://tienda.ejemplo.es</c>).</summary>
    public Uri Url { get; set; } = new("http://localhost:8080");

    /// <summary>Clave de la API REST (<c>ck_...</c>).</summary>
    public string ClaveCliente { get; set; } = string.Empty;

    /// <summary>Secreto de la API REST (<c>cs_...</c>).</summary>
    public string SecretoCliente { get; set; } = string.Empty;

    /// <summary>Secreto con el que WooCommerce firma los webhooks.</summary>
    public string SecretoWebhook { get; set; } = string.Empty;
}

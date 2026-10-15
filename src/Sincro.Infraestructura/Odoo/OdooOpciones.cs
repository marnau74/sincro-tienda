namespace Sincro.Infraestructura.Odoo;

public sealed class OdooOpciones
{
    public const string Seccion = "Odoo";

    public Uri Url { get; set; } = new("http://localhost:8069");

    /// <summary>Base de datos (cabecera <c>X-Odoo-Database</c>); vacío si el servidor solo tiene una.</summary>
    public string BaseDeDatos { get; set; } = string.Empty;

    /// <summary>Clave de API de un usuario dedicado a la integración (Preferencias › Seguridad de la cuenta).</summary>
    public string ClaveApi { get; set; } = string.Empty;

    /// <summary>Referencia interna del producto de servicio con el que se cobran los gastos de envío.</summary>
    public string SkuEnvio { get; set; } = "ENVIO";
}

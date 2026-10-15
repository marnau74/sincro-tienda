namespace Sincro.Dominio.Registro;

/// <summary>
/// Una línea del registro de cambios: qué se hizo, sobre qué y con qué resultado. Es lo que se
/// consulta cuando alguien pregunta «¿por qué este pedido no está en el ERP?».
/// </summary>
public sealed class RegistroCambio
{
    public long Id { get; private set; }
    public DateTimeOffset Fecha { get; private set; }

    /// <summary><c>pedido</c> o <c>stock</c>.</summary>
    public string Entidad { get; private set; } = string.Empty;

    /// <summary>Id del pedido en la tienda o SKU.</summary>
    public string Referencia { get; private set; } = string.Empty;

    /// <summary>Lo que se hizo (p. ej. <c>crear_y_confirmar</c>, <c>stock_actualizado</c>, <c>error</c>).</summary>
    public string Accion { get; private set; } = string.Empty;

    public string Detalle { get; private set; } = string.Empty;

    private RegistroCambio()
    {
    }

    public static RegistroCambio Nuevo(DateTimeOffset fecha, string entidad, string referencia, string accion, string detalle) => new()
    {
        Fecha = fecha,
        Entidad = entidad,
        Referencia = referencia,
        Accion = accion,
        Detalle = detalle.Length > 4000 ? detalle[..4000] : detalle,
    };
}

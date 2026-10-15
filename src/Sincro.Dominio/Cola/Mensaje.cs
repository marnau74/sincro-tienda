namespace Sincro.Dominio.Cola;

public enum TipoMensaje
{
    /// <summary>Un pedido de la tienda ha cambiado: llevarlo al ERP.</summary>
    Pedido,

    /// <summary>El stock de un producto ha cambiado en el ERP: llevarlo a la tienda.</summary>
    Stock,
}

public enum EstadoMensaje
{
    Pendiente,
    Procesando,
    Hecho,

    /// <summary>Agotó los reintentos o el error no se arregla reintentando: espera a una persona.</summary>
    Fallido,
}

/// <summary>
/// Aviso de que algo ha cambiado (un pedido, el stock de un SKU), guardado en PostgreSQL hasta que
/// se procesa. No lleva los datos: quien lo procesa los lee de la fuente en ese momento, así que da
/// igual que lleguen repetidos o desordenados.
/// </summary>
public sealed class Mensaje
{
    public Guid Id { get; private set; }
    public TipoMensaje Tipo { get; private set; }

    /// <summary>Lo que identifica el cambio concreto (p. ej. <c>pedido:1042:2026-10-15T09:30:00Z</c>): no se guarda dos veces.</summary>
    public string ClaveIdempotencia { get; private set; } = string.Empty;

    /// <summary>La entidad (p. ej. <c>pedido:1042</c>): los mensajes de una misma entidad se procesan de uno en uno y en orden.</summary>
    public string ClaveOrden { get; private set; } = string.Empty;

    /// <summary>Identificador de la entidad en su origen (id del pedido o SKU).</summary>
    public string Referencia { get; private set; } = string.Empty;

    public EstadoMensaje Estado { get; private set; }
    public int Intentos { get; private set; }
    public DateTimeOffset ProximoIntento { get; private set; }

    /// <summary>Mientras se procesa, hasta cuándo es de quien lo tomó; después otro proceso puede retomarlo.</summary>
    public DateTimeOffset? BloqueadoHasta { get; private set; }

    public string? UltimoError { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }
    public DateTimeOffset ActualizadoEn { get; private set; }

    private Mensaje()
    {
    }

    public static Mensaje Nuevo(TipoMensaje tipo, string referencia, string version, DateTimeOffset ahora)
    {
        var entidad = $"{tipo.ToString().ToLowerInvariant()}:{referencia}";
        return new Mensaje
        {
            Id = Guid.CreateVersion7(ahora),
            Tipo = tipo,
            Referencia = referencia,
            ClaveOrden = entidad,
            ClaveIdempotencia = $"{entidad}:{version}",
            Estado = EstadoMensaje.Pendiente,
            ProximoIntento = ahora,
            CreadoEn = ahora,
            ActualizadoEn = ahora,
        };
    }

    public void Tomar(DateTimeOffset ahora, TimeSpan duracion)
    {
        Estado = EstadoMensaje.Procesando;
        BloqueadoHasta = ahora + duracion;
        ActualizadoEn = ahora;
    }

    public void MarcarHecho(DateTimeOffset ahora)
    {
        Estado = EstadoMensaje.Hecho;
        BloqueadoHasta = null;
        UltimoError = null;
        ActualizadoEn = ahora;
    }

    /// <summary>
    /// Anota un fallo: si es permanente o se agotaron los intentos queda <see cref="EstadoMensaje.Fallido"/>;
    /// si no, vuelve a la cola con una espera creciente. <paramref name="azar"/> (0-1) reparte los
    /// reintentos para que no vuelvan todos a la vez.
    /// </summary>
    public void RegistrarFallo(string error, bool permanente, DateTimeOffset ahora, PoliticaReintentos politica, double azar)
    {
        Intentos++;
        UltimoError = error.Length > 2000 ? error[..2000] : error;
        BloqueadoHasta = null;
        ActualizadoEn = ahora;
        if (permanente || Intentos >= politica.MaxIntentos)
        {
            Estado = EstadoMensaje.Fallido;
            return;
        }
        Estado = EstadoMensaje.Pendiente;
        ProximoIntento = ahora + politica.EsperaTras(Intentos, azar);
    }

    /// <summary>Reintento a mano de un mensaje fallido (p. ej. tras crear en el ERP el SKU que faltaba).</summary>
    public bool Reintentar(DateTimeOffset ahora)
    {
        if (Estado != EstadoMensaje.Fallido)
        {
            return false;
        }
        Estado = EstadoMensaje.Pendiente;
        Intentos = 0;
        ProximoIntento = ahora;
        ActualizadoEn = ahora;
        return true;
    }
}

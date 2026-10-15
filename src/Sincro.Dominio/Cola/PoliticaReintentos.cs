namespace Sincro.Dominio.Cola;

/// <summary>
/// Reintentos con espera exponencial: 30 s, 1 min, 2 min, 4 min... hasta un máximo, con un ±20 %
/// al azar para que los mensajes que fallaron juntos (p. ej. con el ERP caído) no vuelvan a la vez.
/// </summary>
public sealed record PoliticaReintentos(int MaxIntentos, TimeSpan EsperaInicial, TimeSpan EsperaMaxima)
{
    public static readonly PoliticaReintentos PorDefecto = new(8, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));

    /// <param name="intento">Intentos ya hechos (1 = tras el primer fallo).</param>
    /// <param name="azar">Número entre 0 y 1.</param>
    public TimeSpan EsperaTras(int intento, double azar)
    {
        var exponente = Math.Min(intento - 1, 20);
        var segundos = Math.Min(EsperaInicial.TotalSeconds * Math.Pow(2, exponente), EsperaMaxima.TotalSeconds);
        return TimeSpan.FromSeconds(segundos * (0.8 + 0.4 * Math.Clamp(azar, 0, 1)));
    }
}

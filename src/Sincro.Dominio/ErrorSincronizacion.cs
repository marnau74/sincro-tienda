namespace Sincro.Dominio;

/// <summary>
/// Error al sincronizar. <see cref="Permanente"/> distingue lo que se arregla solo reintentando
/// (ERP caído, límite de peticiones) de lo que necesita a una persona (un SKU que no existe en el
/// ERP, credenciales mal puestas): esto último no gasta reintentos y queda fallido al momento.
/// </summary>
public sealed class ErrorSincronizacion(string mensaje, bool permanente, Exception? causa = null)
    : Exception(mensaje, causa)
{
    public bool Permanente { get; } = permanente;
}

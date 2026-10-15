namespace Sincro.Dominio.Pedidos;

/// <summary>Estado del pedido en el ERP (<see cref="NoExiste"/> si aún no se ha creado).</summary>
public enum EstadoPedidoErp
{
    NoExiste,
    Presupuesto,
    Confirmado,
    Cancelado,
}

public enum AccionErp
{
    Ninguna,
    CrearPresupuesto,
    CrearYConfirmar,
    Confirmar,
    Cancelar,
}

/// <summary>
/// Qué hacer en el ERP según el estado del pedido en la tienda y en el ERP. Es la misma decisión
/// para un pedido nuevo, uno repetido o uno que llega desordenado: siempre se compara con lo que
/// ya hay, nunca se aplica a ciegas (así reintentar no duplica nada).
/// </summary>
public static class ReglasPedido
{
    public static AccionErp AccionPara(EstadoPedidoTienda tienda, EstadoPedidoErp erp) => tienda switch
    {
        // Pagado: tiene que estar confirmado
        EstadoPedidoTienda.Procesando or EstadoPedidoTienda.Completado => erp switch
        {
            EstadoPedidoErp.NoExiste => AccionErp.CrearYConfirmar,
            EstadoPedidoErp.Presupuesto => AccionErp.Confirmar,
            _ => AccionErp.Ninguna, // confirmado, o cancelado a mano en el ERP: no se reabre
        },
        // Sin pagar todavía: basta con un presupuesto
        EstadoPedidoTienda.Pendiente or EstadoPedidoTienda.EnEspera => erp == EstadoPedidoErp.NoExiste
            ? AccionErp.CrearPresupuesto
            : AccionErp.Ninguna,
        // Anulado en la tienda: se cancela si existe y no lo está
        EstadoPedidoTienda.Cancelado or EstadoPedidoTienda.Reembolsado or EstadoPedidoTienda.Fallido =>
            erp is EstadoPedidoErp.Presupuesto or EstadoPedidoErp.Confirmado ? AccionErp.Cancelar : AccionErp.Ninguna,
        _ => AccionErp.Ninguna, // borradores y estados desconocidos no se llevan al ERP
    };
}

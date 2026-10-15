using Sincro.Dominio.Registro;

namespace Sincro.Aplicacion.Stock;

/// <summary>
/// Pone en la tienda el stock que tiene ahora el ERP para un SKU. Se lee en el momento (no se fía
/// del valor que había al detectar el cambio), así que repetirlo o hacerlo tarde da lo mismo.
/// </summary>
public sealed class ProcesarStock(ITienda tienda, IErp erp, IRegistroCambios registro, TimeProvider reloj)
{
    public async Task<string> Ejecutar(string sku, CancellationToken ct)
    {
        var cantidad = await erp.LeerStock(sku, ct);
        if (cantidad is null)
        {
            return await Anotar(sku, "sin_producto_en_erp", "El SKU no existe en el ERP", ct);
        }
        var idProducto = await tienda.BuscarProductoPorSku(sku, ct);
        if (idProducto is null)
        {
            return await Anotar(sku, "no_se_vende_online", "La tienda no tiene ningún producto con este SKU", ct);
        }
        // La tienda solo admite unidades enteras y nunca negativas (en el ERP puede haber reservas por encima)
        var unidades = (int)Math.Floor(Math.Max(0, cantidad.Value));
        await tienda.ActualizarStock(idProducto.Value, unidades, ct);
        return await Anotar(sku, "stock_actualizado", $"{unidades} unidades (producto {idProducto} de la tienda)", ct);
    }

    private async Task<string> Anotar(string sku, string accion, string detalle, CancellationToken ct)
    {
        await registro.Anotar(RegistroCambio.Nuevo(reloj.GetUtcNow(), "stock", sku, accion, detalle), ct);
        return accion;
    }
}

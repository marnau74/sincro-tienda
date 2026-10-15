using Shouldly;

using Sincro.Dominio.Cola;
using Sincro.Dominio.Pedidos;

namespace Sincro.Unitarios.Tests;

public class ReglasPedidoTests
{
    [Theory]
    [InlineData(EstadoPedidoTienda.Procesando, EstadoPedidoErp.NoExiste, AccionErp.CrearYConfirmar)]
    [InlineData(EstadoPedidoTienda.Procesando, EstadoPedidoErp.Presupuesto, AccionErp.Confirmar)]
    [InlineData(EstadoPedidoTienda.Procesando, EstadoPedidoErp.Confirmado, AccionErp.Ninguna)]
    [InlineData(EstadoPedidoTienda.Completado, EstadoPedidoErp.NoExiste, AccionErp.CrearYConfirmar)]
    [InlineData(EstadoPedidoTienda.Procesando, EstadoPedidoErp.Cancelado, AccionErp.Ninguna)]
    [InlineData(EstadoPedidoTienda.Pendiente, EstadoPedidoErp.NoExiste, AccionErp.CrearPresupuesto)]
    [InlineData(EstadoPedidoTienda.EnEspera, EstadoPedidoErp.Presupuesto, AccionErp.Ninguna)]
    [InlineData(EstadoPedidoTienda.Cancelado, EstadoPedidoErp.Confirmado, AccionErp.Cancelar)]
    [InlineData(EstadoPedidoTienda.Reembolsado, EstadoPedidoErp.Presupuesto, AccionErp.Cancelar)]
    [InlineData(EstadoPedidoTienda.Cancelado, EstadoPedidoErp.NoExiste, AccionErp.Ninguna)]
    [InlineData(EstadoPedidoTienda.Cancelado, EstadoPedidoErp.Cancelado, AccionErp.Ninguna)]
    [InlineData(EstadoPedidoTienda.Borrador, EstadoPedidoErp.NoExiste, AccionErp.Ninguna)]
    public void Decide_comparando_tienda_y_erp(EstadoPedidoTienda tienda, EstadoPedidoErp erp, AccionErp esperada) =>
        ReglasPedido.AccionPara(tienda, erp).ShouldBe(esperada);

    [Fact]
    public void Repetir_la_accion_tras_aplicarla_no_hace_nada_mas()
    {
        // Lo que deja cada acción en el ERP vuelve a decidir «nada»: reintentar es seguro
        ReglasPedido.AccionPara(EstadoPedidoTienda.Procesando, EstadoPedidoErp.Confirmado).ShouldBe(AccionErp.Ninguna);
        ReglasPedido.AccionPara(EstadoPedidoTienda.Pendiente, EstadoPedidoErp.Presupuesto).ShouldBe(AccionErp.Ninguna);
        ReglasPedido.AccionPara(EstadoPedidoTienda.Cancelado, EstadoPedidoErp.Cancelado).ShouldBe(AccionErp.Ninguna);
    }
}

public class PedidoTiendaTests
{
    [Fact]
    public void Un_pedido_correcto_no_tiene_errores() => Ejemplos.Pedido().Validar().ShouldBeEmpty();

    [Fact]
    public void Lineas_sin_sku_o_sin_cantidad_y_sin_email_son_errores()
    {
        var pedido = Ejemplos.Pedido(sku: " ") with
        {
            Cliente = Ejemplos.Pedido().Cliente with { Email = "" },
            Lineas = [new LineaPedido(" ", "Sin SKU", 1, 1), new LineaPedido("X", "Cero", 0, 1)],
        };
        pedido.Validar().Count.ShouldBe(3);
    }

    [Fact]
    public void La_referencia_en_el_erp_sale_del_id() => Ejemplos.Pedido(1042).Referencia.ShouldBe("WC-1042");
}

public class MensajeTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Las_claves_identifican_el_cambio_y_la_entidad()
    {
        var m = Mensaje.Nuevo(TipoMensaje.Pedido, "1042", "v1", Ahora);
        m.ClaveOrden.ShouldBe("pedido:1042");
        m.ClaveIdempotencia.ShouldBe("pedido:1042:v1");
        m.Estado.ShouldBe(EstadoMensaje.Pendiente);
        m.ProximoIntento.ShouldBe(Ahora);
    }

    [Fact]
    public void Un_fallo_pasajero_vuelve_a_la_cola_con_espera()
    {
        var m = Mensaje.Nuevo(TipoMensaje.Stock, "CAF-250", "v1", Ahora);
        m.Tomar(Ahora, TimeSpan.FromMinutes(5));
        m.RegistrarFallo("timeout", permanente: false, Ahora, PoliticaReintentos.PorDefecto, azar: 0.5);
        m.Estado.ShouldBe(EstadoMensaje.Pendiente);
        m.Intentos.ShouldBe(1);
        m.ProximoIntento.ShouldBe(Ahora.AddSeconds(30));
        m.BloqueadoHasta.ShouldBeNull();
        m.UltimoError.ShouldBe("timeout");
    }

    [Fact]
    public void Un_error_permanente_queda_fallido_sin_gastar_reintentos()
    {
        var m = Mensaje.Nuevo(TipoMensaje.Pedido, "1", "v1", Ahora);
        m.RegistrarFallo("SKU X no existe en el ERP", permanente: true, Ahora, PoliticaReintentos.PorDefecto, 0.5);
        m.Estado.ShouldBe(EstadoMensaje.Fallido);
    }

    [Fact]
    public void Agotados_los_intentos_queda_fallido_y_se_puede_reintentar_a_mano()
    {
        var politica = new PoliticaReintentos(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
        var m = Mensaje.Nuevo(TipoMensaje.Pedido, "1", "v1", Ahora);
        for (var i = 0; i < 3; i++)
        {
            m.RegistrarFallo("caído", false, Ahora, politica, 0.5);
        }
        m.Estado.ShouldBe(EstadoMensaje.Fallido);

        m.Reintentar(Ahora.AddHours(1)).ShouldBeTrue();
        m.Estado.ShouldBe(EstadoMensaje.Pendiente);
        m.Intentos.ShouldBe(0);
        m.Reintentar(Ahora).ShouldBeFalse(); // solo los fallidos
    }
}

public class PoliticaReintentosTests
{
    [Fact]
    public void La_espera_se_duplica_hasta_el_maximo()
    {
        var p = PoliticaReintentos.PorDefecto;
        p.EsperaTras(1, 0.5).ShouldBe(TimeSpan.FromSeconds(30));
        p.EsperaTras(2, 0.5).ShouldBe(TimeSpan.FromMinutes(1));
        p.EsperaTras(4, 0.5).ShouldBe(TimeSpan.FromMinutes(4));
        p.EsperaTras(20, 0.5).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void El_azar_reparte_un_20_por_ciento_arriba_o_abajo()
    {
        var p = PoliticaReintentos.PorDefecto;
        p.EsperaTras(1, 0).ShouldBe(TimeSpan.FromSeconds(24));
        p.EsperaTras(1, 1).ShouldBe(TimeSpan.FromSeconds(36));
    }
}

namespace BilleteraSellos.Dominio.Tests;

/// <summary>Sellos por monto de compra, sellos que sobran y el QR de la compra.</summary>
public class SellosPorCompraTests
{
    static ProgramaSellos Programa(decimal? montoPorSello = null, decimal? compraMinima = null, int sellos = 10)
    {
        var p = ProgramaSellos.Nuevo(sellos, "$5.000 de descuento", 5000);
        Assert.True(p.Actualizar(sellos, "$5.000 de descuento", 5000, 7, 0, montoPorSello, compraMinima).Exito);
        return p;
    }

    [Fact]
    public void Sin_reglas_de_monto_cada_compra_da_un_sello_con_o_sin_monto()
    {
        var p = Programa();

        Assert.Equal(1, p.SellosPorCompra(null).Valor);
        Assert.Equal(1, p.SellosPorCompra(500).Valor);
        Assert.Equal(1, p.SellosPorCompra(1_000_000).Valor);
    }

    [Theory]
    [InlineData(10000, 1)]
    [InlineData(19999.99, 1)]
    [InlineData(20000, 2)]
    [InlineData(35000, 3)]
    [InlineData(300000, 30)]
    public void Con_monto_por_sello_da_un_sello_cada_X(double monto, int esperados)
    {
        var p = Programa(montoPorSello: 10000);

        Assert.Equal(esperados, p.SellosPorCompra((decimal)monto).Valor);
    }

    [Fact]
    public void Con_monto_por_sello_el_monto_es_obligatorio()
    {
        var r = Programa(montoPorSello: 10000).SellosPorCompra(null);

        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal("Escribí el monto de la compra.", r.Error.Mensaje);
    }

    [Fact]
    public void Con_compra_minima_el_monto_tambien_es_obligatorio()
    {
        Assert.Equal("Escribí el monto de la compra.", Programa(compraMinima: 5000).SellosPorCompra(null).Error!.Mensaje);
    }

    [Fact]
    public void Si_no_alcanza_para_un_sello_explica_cuanto_hace_falta()
    {
        var r = Programa(montoPorSello: 10000).SellosPorCompra(9999);

        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal("Con ese monto no alcanza para un sello (1 sello cada $10.000).", r.Error.Mensaje);
    }

    [Fact]
    public void Debajo_de_la_compra_minima_no_suma()
    {
        var p = Programa(montoPorSello: 10000, compraMinima: 15000);

        Assert.Equal("La compra mínima para sumar sellos es de $15.000.", p.SellosPorCompra(14999).Error!.Mensaje);
        Assert.Equal(1, p.SellosPorCompra(15000).Valor);
    }

    [Fact]
    public void Compra_minima_sin_monto_por_sello_da_un_sello_si_llega()
    {
        var p = Programa(compraMinima: 5000);

        Assert.False(p.SellosPorCompra(4999).Exito);
        Assert.Equal(1, p.SellosPorCompra(5000).Valor);
        Assert.Equal(1, p.SellosPorCompra(80000).Valor);
    }

    [Fact]
    public void Un_monto_absurdo_se_frena_por_el_tope_de_sellos()
    {
        var r = Programa(montoPorSello: 10000).SellosPorCompra(35_000_000);

        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal($"Esa compra daría 3500 sellos. Revisá el monto (máximo {ProgramaSellos.MaxSellosPorCompra} por compra).", r.Error.Mensaje);
        Assert.False(Programa(montoPorSello: 10000).SellosPorCompra(310000).Exito);
    }

    [Fact]
    public void Un_monto_negativo_no_vale()
    {
        Assert.Equal("El monto no puede ser negativo.", Programa().SellosPorCompra(-1).Error!.Mensaje);
    }

    [Fact]
    public void Los_montos_se_muestran_como_en_Argentina_sin_depender_del_servidor()
    {
        Assert.Equal("$10.000", Pesos.Formatear(10000));
        Assert.Equal("$1.250,5", Pesos.Formatear(1250.5m));
        Assert.Equal("$500", Pesos.Formatear(500));
    }

    [Theory]
    [InlineData(0, "El monto por sello tiene que ser mayor a cero.")]
    [InlineData(-100, "El monto por sello tiene que ser mayor a cero.")]
    public void Monto_por_sello_invalido(double monto, string mensaje)
    {
        var p = ProgramaSellos.Nuevo(10, "Premio", 0);

        var r = p.Actualizar(10, "Premio", 0, 7, 1, (decimal)monto, null);

        Assert.Equal(mensaje, r.Error!.Mensaje);
        Assert.Null(p.MontoPorSello);
    }

    [Fact]
    public void Compra_minima_negativa_no_vale()
    {
        Assert.Equal("La compra mínima no puede ser negativa.",
            ProgramaSellos.Nuevo(10, "Premio", 0).Actualizar(10, "Premio", 0, 7, 1, null, -1).Error!.Mensaje);
    }

    [Fact]
    public void Actualizar_sin_reglas_de_monto_las_borra()
    {
        var p = Programa(montoPorSello: 10000, compraMinima: 5000);

        Assert.True(p.Actualizar(10, "Premio", 0, 7, 1).Exito);

        Assert.Null(p.MontoPorSello);
        Assert.Null(p.CompraMinima);
    }
}

public class SellosQueSobranTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static ProgramaSellos Programa(int sellos = 10)
    {
        var p = ProgramaSellos.Nuevo(sellos, "Premio", 0);
        Assert.True(p.Actualizar(sellos, "Premio", 0, 7, 0).Exito);
        return p;
    }

    [Fact]
    public void Una_compra_suma_varios_sellos_en_un_solo_movimiento_con_el_monto()
    {
        var p = Programa();
        var t = Tarjeta.Abrir(1, 1, Ahora);

        Assert.True(t.SumarSellos(p, 3, 35000, Ahora, "Ana (caja 1)").Exito);

        Assert.Equal(3, t.SellosActuales);
        Assert.Equal(3, t.SellosTotales);
        var m = Assert.Single(t.Movimientos);
        Assert.Equal(3, m.Cantidad);
        Assert.Equal(35000, m.Monto);
        Assert.Equal("Ana (caja 1)", m.CargadoPor);
        // Una compra es una visita, aunque dé varios sellos.
        Assert.Single(t.Visitas());
    }

    [Fact]
    public void Si_faltaba_uno_y_la_compra_da_tres_los_dos_que_sobran_quedan()
    {
        var p = Programa(sellos: 10);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSellos(p, 9, null, Ahora.AddDays(-1), "caja").Exito);

        Assert.True(t.SumarSellos(p, 3, 30000, Ahora, "caja").Exito);
        var canje = t.Canjear(p, "ABC234", Ahora);

        Assert.True(canje.Exito);
        Assert.Equal(2, t.SellosActuales);
        Assert.Equal(12, t.SellosTotales);
    }

    [Fact]
    public void Con_sellos_para_dos_premios_se_puede_canjear_dos_veces()
    {
        var p = Programa(sellos: 5);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSellos(p, 11, null, Ahora, "caja").Exito);

        Assert.Equal(2, t.PremiosDisponibles(p));
        Assert.True(t.Canjear(p, "AAA234", Ahora).Exito);
        Assert.True(t.Canjear(p, "BBB234", Ahora).Exito);
        Assert.Equal(1, t.SellosActuales);
        Assert.False(t.Canjear(p, "CCC234", Ahora).Exito);
    }

    [Fact]
    public void Cantidad_menor_a_uno_no_vale()
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);

        var r = t.SumarSellos(Programa(), 0, null, Ahora, "caja");

        Assert.Equal("La cantidad de sellos tiene que ser al menos 1.", r.Error!.Mensaje);
        Assert.Empty(t.Movimientos);
    }

    [Fact]
    public void La_espera_entre_sellos_tambien_aplica_a_compras_de_varios_sellos()
    {
        var p = ProgramaSellos.Nuevo(10, "Premio", 0);
        Assert.True(p.Actualizar(10, "Premio", 0, 7, 2).Exito);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSellos(p, 2, null, Ahora, "caja").Exito);

        Assert.False(t.SumarSellos(p, 3, null, Ahora.AddHours(1), "caja").Exito);
        Assert.Equal(2, t.SellosActuales);
    }
}

public class CompraQrTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static CompraQr Nueva() => CompraQr.Generar(comercioId: 5, usuarioId: 7, codigoHash: "HASH", monto: 35000, sellos: 3, ahora: Ahora);

    [Fact]
    public void Se_genera_pendiente_y_vence_a_los_tres_minutos()
    {
        var c = Nueva();

        Assert.Equal(EstadoCompraQr.Pendiente, c.Estado(Ahora));
        Assert.Equal(Ahora.AddMinutes(3), c.VenceEn);
        Assert.Equal(3, c.Sellos);
        Assert.Equal(35000, c.Monto);
    }

    [Fact]
    public void Usarlo_registra_quien_y_cuando()
    {
        var c = Nueva();

        Assert.True(c.Usar(clienteId: 1, Ahora.AddMinutes(1)).Exito);

        Assert.Equal(EstadoCompraQr.Usada, c.Estado(Ahora.AddMinutes(1)));
        Assert.Equal(1, c.ClienteId);
        Assert.Equal(Ahora.AddMinutes(1), c.UsadaEn);
    }

    [Fact]
    public void No_sirve_dos_veces()
    {
        var c = Nueva();
        Assert.True(c.Usar(1, Ahora).Exito);

        var r = c.Usar(2, Ahora.AddSeconds(10));

        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("Ese QR ya se usó.", r.Error.Mensaje);
        Assert.Equal(1, c.ClienteId);
    }

    [Fact]
    public void En_el_instante_exacto_del_vencimiento_ya_no_vale()
    {
        var c = Nueva();

        Assert.True(c.PuedeUsarse(c.VenceEn.AddTicks(-1)).Exito);
        Assert.Equal(EstadoCompraQr.Vencida, c.Estado(c.VenceEn));
        Assert.Equal("Ese QR venció. Pedile al cajero uno nuevo.", c.Usar(1, c.VenceEn).Error!.Mensaje);
        Assert.Null(c.UsadaEn);
    }

    [Fact]
    public void Anulado_no_se_puede_usar()
    {
        var c = Nueva();
        Assert.True(c.Anular(Ahora).Exito);

        Assert.Equal(EstadoCompraQr.Anulada, c.Estado(Ahora));
        Assert.Equal("Ese QR fue anulado. Pedile al cajero uno nuevo.", c.Usar(1, Ahora).Error!.Mensaje);
    }

    [Fact]
    public void Usado_no_se_puede_anular_y_sigue_figurando_como_usado()
    {
        var c = Nueva();
        Assert.True(c.Usar(1, Ahora).Exito);

        var r = c.Anular(Ahora);

        Assert.Equal("Ese QR ya se usó: no se puede anular.", r.Error!.Mensaje);
        Assert.Equal(EstadoCompraQr.Usada, c.Estado(Ahora.AddHours(1)));
    }

    [Fact]
    public void Usado_figura_como_usado_aunque_haya_pasado_el_vencimiento()
    {
        var c = Nueva();
        Assert.True(c.Usar(1, Ahora).Exito);

        Assert.Equal(EstadoCompraQr.Usada, c.Estado(Ahora.AddDays(1)));
    }

    [Fact]
    public void PuedeUsarse_no_marca_nada()
    {
        var c = Nueva();

        Assert.True(c.PuedeUsarse(Ahora).Exito);
        Assert.True(c.PuedeUsarse(Ahora).Exito);
        Assert.Null(c.UsadaEn);
    }
}

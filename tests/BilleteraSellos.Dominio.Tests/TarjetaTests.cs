namespace BilleteraSellos.Dominio.Tests;

public class TarjetaTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static ProgramaSellos Programa(int sellos = 10, int horasEntreSellos = 1, int diasParaRetirar = 7)
    {
        var p = ProgramaSellos.Nuevo(sellos, "Café gratis", 900);
        Assert.True(p.Actualizar(sellos, "Café gratis", 900, diasParaRetirar, horasEntreSellos).Exito);
        return p;
    }

    /// <summary>Tarjeta con <paramref name="sellos"/> sellos sumados de a uno, separados por un día.</summary>
    static Tarjeta TarjetaCon(int sellos, ProgramaSellos programa)
    {
        var t = Tarjeta.Abrir(1, 1, Ahora.AddDays(-sellos - 1));
        for (var i = 0; i < sellos; i++)
            Assert.True(t.SumarSello(programa, Ahora.AddDays(-sellos + i), "caja").Exito);
        return t;
    }

    [Fact]
    public void SumarSello_suma_actuales_totales_y_registra_el_movimiento()
    {
        var p = Programa();
        var t = Tarjeta.Abrir(1, 1, Ahora);

        var r = t.SumarSello(p, Ahora, "Juan (caja)");

        Assert.True(r.Exito);
        Assert.Equal(1, t.SellosActuales);
        Assert.Equal(1, t.SellosTotales);
        Assert.Equal(Ahora, t.UltimoSello);
        var m = Assert.Single(t.Movimientos);
        Assert.Equal(TipoMovimiento.Sello, m.Tipo);
        Assert.Equal("Juan (caja)", m.CargadoPor);
    }

    [Fact]
    public void SumarSello_con_tarjeta_completa_devuelve_conflicto()
    {
        var p = Programa(sellos: 3, horasEntreSellos: 0);
        var t = TarjetaCon(3, p);

        var r = t.SumarSello(p, Ahora, "caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("La tarjeta ya está completa. El cliente tiene que canjear el premio antes de sumar más sellos.", r.Error.Mensaje);
        Assert.Equal(3, t.SellosActuales);
    }

    [Fact]
    public void SumarSello_justo_antes_del_tiempo_minimo_devuelve_conflicto()
    {
        var p = Programa(horasEntreSellos: 2);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);

        var r = t.SumarSello(p, Ahora.AddHours(2).AddSeconds(-1), "caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("Ya se sumó un sello hace menos de 2 h. Si es una compra nueva, esperá o cambiá la regla del programa.", r.Error.Mensaje);
        Assert.Equal(1, t.SellosActuales);
    }

    [Fact]
    public void SumarSello_justo_al_cumplirse_el_tiempo_minimo_suma()
    {
        var p = Programa(horasEntreSellos: 2);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);

        var r = t.SumarSello(p, Ahora.AddHours(2), "caja");

        Assert.True(r.Exito);
        Assert.Equal(2, t.SellosActuales);
    }

    [Fact]
    public void SumarSello_sin_tiempo_minimo_permite_dos_sellos_seguidos()
    {
        var p = Programa(horasEntreSellos: 0);
        var t = Tarjeta.Abrir(1, 1, Ahora);

        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);
        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);
        Assert.Equal(2, t.SellosActuales);
    }

    [Fact]
    public void EstaCompleta_cuando_llega_a_los_sellos_necesarios()
    {
        var p = Programa(sellos: 3, horasEntreSellos: 0);
        Assert.False(TarjetaCon(2, p).EstaCompleta(p));
        Assert.True(TarjetaCon(3, p).EstaCompleta(p));
    }

    [Fact]
    public void Canjear_con_tarjeta_completa_descuenta_los_sellos_y_crea_el_canje()
    {
        var p = Programa(sellos: 3, horasEntreSellos: 0, diasParaRetirar: 5);
        var t = TarjetaCon(3, p);

        var r = t.Canjear(p, "ABC234", Ahora);

        Assert.True(r.Exito);
        Assert.Equal(0, t.SellosActuales);
        Assert.Equal(3, t.SellosTotales); // los totales no bajan: definen el nivel
        Assert.Equal("ABC234", r.Valor.Codigo);
        Assert.Equal("Café gratis", r.Valor.Premio);
        Assert.Equal(Ahora.AddDays(5), r.Valor.VenceEn);
        Assert.Null(r.Valor.EntregadoEn);
        Assert.Same(r.Valor, Assert.Single(t.Canjes));
        Assert.Equal(TipoMovimiento.Canje, t.Movimientos[^1].Tipo);
    }

    [Fact]
    public void Canjear_sin_sellos_suficientes_devuelve_conflicto_con_los_que_faltan()
    {
        var p = Programa(sellos: 10, horasEntreSellos: 0);
        var t = TarjetaCon(7, p);

        var r = t.Canjear(p, "ABC234", Ahora);

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("Todavía te faltan 3 sellos.", r.Error.Mensaje);
        Assert.Equal(7, t.SellosActuales);
        Assert.Empty(t.Canjes);
    }

    [Fact]
    public void Despues_de_canjear_se_puede_volver_a_sumar()
    {
        var p = Programa(sellos: 3, horasEntreSellos: 0);
        var t = TarjetaCon(3, p);
        Assert.True(t.Canjear(p, "ABC234", Ahora).Exito);

        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);
        Assert.Equal(1, t.SellosActuales);
        Assert.Equal(4, t.SellosTotales);
    }

    [Theory]
    [InlineData(34, 10, 4)]
    [InlineData(11, 6, 5)]
    [InlineData(4, 8, 4)]
    [InlineData(10, 10, 0)]
    [InlineData(0, 10, 0)]
    public void ImportarHistorial_deja_como_actuales_el_resto_de_las_vueltas(int visitas, int necesarios, int actualesEsperados)
    {
        var fechas = Enumerable.Range(0, visitas).Select(i => Ahora.AddDays(-i * 3)).ToList();

        var t = Tarjeta.ImportarHistorial(ClienteDePrueba(), ComercioDePrueba(), fechas, necesarios, "demo");

        Assert.Equal(visitas, t.SellosTotales);
        Assert.Equal(actualesEsperados, t.SellosActuales);
        Assert.Equal(visitas, t.Movimientos.Count);
        Assert.All(t.Movimientos, m => Assert.Equal("demo", m.CargadoPor));
    }

    [Fact]
    public void ImportarHistorial_usa_la_ultima_visita_como_ultimo_sello_y_la_primera_como_alta()
    {
        var fechas = new[] { Ahora.AddDays(-2), Ahora.AddDays(-10), Ahora.AddDays(-5) };

        var t = Tarjeta.ImportarHistorial(ClienteDePrueba(), ComercioDePrueba(), fechas, 10, "demo");

        Assert.Equal(Ahora.AddDays(-2), t.UltimoSello);
        Assert.Equal(Ahora.AddDays(-10), t.CreadaEn);
        Assert.Equal(fechas.OrderBy(f => f).ToList(), t.Visitas());
    }

    [Theory]
    [InlineData(0, Nivel.Bronce)]
    [InlineData(9, Nivel.Bronce)]
    [InlineData(10, Nivel.Plata)]
    [InlineData(29, Nivel.Plata)]
    [InlineData(30, Nivel.Oro)]
    public void Nivel_depende_de_los_sellos_totales(int totales, Nivel esperado)
    {
        var fechas = Enumerable.Range(0, totales).Select(i => Ahora.AddDays(-i));
        var t = Tarjeta.ImportarHistorial(ClienteDePrueba(), ComercioDePrueba(), fechas, 1000, "demo");
        Assert.Equal(esperado, t.Nivel);
    }

    [Fact]
    public void Visitas_cuenta_solo_los_sellos_y_no_los_canjes()
    {
        var p = Programa(sellos: 3, horasEntreSellos: 0);
        var t = TarjetaCon(3, p);
        Assert.True(t.Canjear(p, "ABC234", Ahora).Exito);

        Assert.Equal(3, t.Visitas().Count);
    }

    [Fact]
    public void CambiarPromos_guarda_el_consentimiento()
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.AceptaPromos);
        t.CambiarPromos(false);
        Assert.False(t.AceptaPromos);
    }

    internal static Cliente ClienteDePrueba() =>
        Cliente.Registrar("Lucía Fernández", "30111222", "1154873320", null, new DateOnly(2026, 10, 15)).Valor;

    internal static Comercio ComercioDePrueba() =>
        Comercio.Crear("Café Tostado", "Cafetería", "Av. Rivadavia 4120", "cafe-tostado", null, ProgramaSellos.Nuevo(10, "Café gratis", 900));
}

public class CanjeTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static Canje CanjeNuevo(int diasParaRetirar = 7)
    {
        var p = ProgramaSellos.Nuevo(3, "Corte gratis", 6000);
        Assert.True(p.Actualizar(3, "Corte gratis", 6000, diasParaRetirar, 0).Exito);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        for (var i = 0; i < 3; i++) Assert.True(t.SumarSello(p, Ahora, "caja").Exito);
        return t.Canjear(p, "XYZ789", Ahora).Valor;
    }

    [Fact]
    public void Entregar_marca_la_fecha_y_quien_lo_entrego()
    {
        var c = CanjeNuevo();

        var r = c.Entregar(Ahora.AddDays(1), "Nico (dueño)");

        Assert.True(r.Exito);
        Assert.Equal(Ahora.AddDays(1), c.EntregadoEn);
        Assert.Equal("Nico (dueño)", c.EntregadoPor);
        Assert.False(c.EstaPendiente(Ahora.AddDays(1)));
    }

    [Fact]
    public void Entregar_dos_veces_devuelve_conflicto()
    {
        var c = CanjeNuevo();
        var primera = new DateTime(2026, 10, 16, 18, 30, 0, DateTimeKind.Utc);
        Assert.True(c.Entregar(primera, "caja").Exito);

        var r = c.Entregar(primera.AddMinutes(5), "otra caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal($"Ese premio ya se entregó el {primera:dd/MM HH:mm}.", r.Error.Mensaje);
        Assert.Equal("caja", c.EntregadoPor);
    }

    [Fact]
    public void Entregar_un_codigo_vencido_devuelve_conflicto()
    {
        var c = CanjeNuevo(diasParaRetirar: 7);

        var r = c.Entregar(Ahora.AddDays(7).AddMinutes(1), "caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal($"El código venció el {c.VenceEn:dd/MM}.", r.Error.Mensaje);
        Assert.Null(c.EntregadoEn);
    }

    [Fact]
    public void Entregar_en_el_instante_exacto_del_vencimiento_ya_no_vale_igual_que_EstaPendiente()
    {
        var c = CanjeNuevo(diasParaRetirar: 7);

        Assert.False(c.EstaPendiente(c.VenceEn));
        Assert.False(c.Entregar(c.VenceEn, "caja").Exito);
        Assert.True(c.Entregar(c.VenceEn.AddTicks(-1), "caja").Exito);
    }

    [Fact]
    public void EstaPendiente_hasta_que_vence()
    {
        var c = CanjeNuevo(diasParaRetirar: 7);
        Assert.True(c.EstaPendiente(Ahora.AddDays(6)));
        Assert.False(c.EstaPendiente(Ahora.AddDays(7)));
    }
}

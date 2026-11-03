namespace BilleteraSellos.Dominio.Tests;

/// <summary>Pasar los sellos de la tarjeta de cartón a la tarjeta digital.</summary>
public class TarjetaDePapelTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static ProgramaSellos Programa(int sellos = 10, int horasEntreSellos = 0)
    {
        var p = ProgramaSellos.Nuevo(sellos, "Café gratis", 900);
        Assert.True(p.Actualizar(sellos, "Café gratis", 900, 7, horasEntreSellos).Exito);
        return p;
    }

    [Fact]
    public void Migrar_suma_los_sellos_actuales_y_totales()
    {
        var p = Programa();
        var t = Tarjeta.Abrir(1, 1, Ahora);

        var r = t.MigrarSellosDePapel(p, 4, Ahora, "Juan (caja)");

        Assert.True(r.Exito);
        Assert.Equal(4, t.SellosActuales);
        Assert.Equal(4, t.SellosTotales);
        var m = Assert.Single(t.Movimientos);
        Assert.Equal(TipoMovimiento.Migracion, m.Tipo);
        Assert.Equal(4, m.Cantidad);
        Assert.Equal("Juan (caja)", m.CargadoPor);
        Assert.True(t.TienePapelMigrado);
    }

    [Fact]
    public void Migrar_no_cuenta_como_visita()
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);

        Assert.True(t.MigrarSellosDePapel(Programa(), 3, Ahora, "caja").Exito);

        Assert.Empty(t.Visitas());
        Assert.Null(t.UltimoSello);
    }

    [Fact]
    public void Se_puede_migrar_una_sola_vez_por_tarjeta()
    {
        var p = Programa();
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.MigrarSellosDePapel(p, 2, Ahora, "caja").Exito);

        var r = t.MigrarSellosDePapel(p, 2, Ahora.AddDays(1), "caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("A esta tarjeta ya se le cargaron los sellos de papel.", r.Error.Mensaje);
        Assert.Equal(2, t.SellosActuales);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(999)]
    public void Migrar_valida_la_cantidad_entre_1_y_dos_tarjetas(int sellos)
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);

        var r = t.MigrarSellosDePapel(Programa(sellos: 10), sellos, Ahora, "caja");

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal("Podés cargar entre 1 y 20 sellos de la tarjeta de papel.", r.Error.Mensaje);
        Assert.Empty(t.Movimientos);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void Migrar_acepta_los_bordes(int sellos)
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);

        Assert.True(t.MigrarSellosDePapel(Programa(sellos: 10), sellos, Ahora, "caja").Exito);
        Assert.Equal(sellos, t.SellosActuales);
    }

    [Fact]
    public void Migrar_no_respeta_la_espera_entre_sellos()
    {
        var p = Programa(horasEntreSellos: 24);
        var t = Tarjeta.Abrir(1, 1, Ahora);
        Assert.True(t.SumarSello(p, Ahora, "caja").Exito);

        Assert.True(t.MigrarSellosDePapel(p, 3, Ahora.AddMinutes(1), "caja").Exito);
        Assert.Equal(4, t.SellosActuales);
    }

    [Fact]
    public void Migrar_puede_completar_la_tarjeta_y_el_sobrante_queda()
    {
        var p = Programa(sellos: 10);
        var t = Tarjeta.Abrir(1, 1, Ahora);

        Assert.True(t.MigrarSellosDePapel(p, 12, Ahora, "caja").Exito);

        Assert.True(t.EstaCompleta(p));
        Assert.Equal(1, t.PremiosDisponibles(p));
        Assert.Equal(12, t.SellosActuales);
    }
}

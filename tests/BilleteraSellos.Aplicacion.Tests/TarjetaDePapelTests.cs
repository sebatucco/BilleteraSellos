using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

namespace BilleteraSellos.Aplicacion.Tests;

/// <summary>La caja carga los sellos de la tarjeta de cartón del cliente.</summary>
public class TarjetaDePapelTests : BaseDePrueba
{
    MigrarTarjetaDePapelHandler Migrar() => new(Db, Notificador, Reloj);

    [Fact]
    public async Task Crea_la_tarjeta_si_no_existia_y_carga_los_sellos()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        var r = await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 4, "Juan (caja)"), default);

        Assert.True(r.Exito);
        Assert.True(r.Valor.TarjetaNueva);
        Assert.Equal(4, r.Valor.SellosActuales);
        Assert.Equal(10, r.Valor.SellosNecesarios);
        Assert.Equal(4, r.Valor.SellosSumados);
        Assert.False(r.Valor.TarjetaCompleta);
        var t = await NuevoContexto().Tarjetas.Include(x => x.Movimientos).SingleAsync();
        Assert.Equal(4, t.SellosActuales);
        var m = Assert.Single(t.Movimientos);
        Assert.Equal(TipoMovimiento.Migracion, m.Tipo);
        Assert.Equal("Juan (caja)", m.CargadoPor);
        Assert.Empty(Notificador.Enviados);
    }

    [Fact]
    public async Task Suma_a_una_tarjeta_digital_que_ya_tenia_sellos()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();
        await TarjetaConHistorialAsync(lucia, cafe, visitas: 3, cadaDias: 2, ultimaHaceDias: 1);

        var r = await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 5, "caja"), default);

        Assert.True(r.Exito);
        Assert.False(r.Valor.TarjetaNueva);
        Assert.Equal(8, r.Valor.SellosActuales);
    }

    [Fact]
    public async Task La_segunda_carga_da_conflicto_y_no_suma()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();
        Assert.True((await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 2, "caja"), default)).Exito);

        var r = await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 2, "caja"), default);

        AssertError(r, TipoError.Conflicto, "A esta tarjeta ya se le cargaron los sellos de papel.");
        Assert.Equal(2, (await NuevoContexto().Tarjetas.SingleAsync()).SellosActuales);
    }

    [Fact]
    public async Task Si_completa_la_tarjeta_avisa_al_cliente()
    {
        var cafe = await ComercioAsync(sellos: 6);
        var lucia = await ClienteAsync();

        var r = await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 6, "caja"), default);

        Assert.True(r.Valor.TarjetaCompleta);
        var aviso = Assert.Single(Notificador.Enviados);
        Assert.Equal(TipoAviso.TarjetaCompleta, aviso.Tipo);
        Assert.Single(await NuevoContexto().Avisos.ToListAsync());
    }

    [Fact]
    public async Task Valida_la_cantidad_y_no_crea_la_tarjeta()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        AssertError(await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 0, "caja"), default),
            TipoError.Validacion, "Podés cargar entre 1 y 20 sellos de la tarjeta de papel.");
        AssertError(await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, lucia.Id, 999, "caja"), default), TipoError.Validacion);
        Assert.Empty(await NuevoContexto().Tarjetas.ToListAsync());
    }

    [Fact]
    public async Task Cliente_o_comercio_inexistente_da_no_encontrado()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        AssertError(await Migrar().Handle(new MigrarTarjetaDePapel(cafe.Id, 999, 3, "caja"), default), TipoError.NoEncontrado, "El cliente no existe.");
        AssertError(await Migrar().Handle(new MigrarTarjetaDePapel(999, lucia.Id, 3, "caja"), default), TipoError.NoEncontrado);
    }
}

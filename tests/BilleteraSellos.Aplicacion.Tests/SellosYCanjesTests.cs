using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;
using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

namespace BilleteraSellos.Aplicacion.Tests;

public class SumarSelloTests : BaseDePrueba
{
    SumarSelloHandler Handler() => new(Db, Notificador, Reloj);

    [Fact]
    public async Task El_primer_sello_crea_la_tarjeta()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        var r = await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "Juan (caja)"), default);

        Assert.True(r.Exito);
        Assert.True(r.Valor.TarjetaNueva);
        Assert.Equal(1, r.Valor.SellosActuales);
        Assert.Equal(10, r.Valor.SellosNecesarios);
        Assert.False(r.Valor.TarjetaCompleta);
        var t = await NuevoContexto().Tarjetas.Include(x => x.Movimientos).SingleAsync();
        Assert.Equal(lucia.Id, t.ClienteId);
        Assert.Equal(cafe.Id, t.ComercioId);
        Assert.Equal("Juan (caja)", Assert.Single(t.Movimientos).CargadoPor);
    }

    [Fact]
    public async Task El_segundo_sello_usa_la_misma_tarjeta()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();
        await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);

        var r = await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);

        Assert.False(r.Valor.TarjetaNueva);
        Assert.Equal(2, r.Valor.SellosActuales);
        Assert.Equal(1, await NuevoContexto().Tarjetas.CountAsync());
    }

    [Fact]
    public async Task Al_completar_la_tarjeta_avisa_al_cliente_y_registra_el_aviso()
    {
        var barberia = await ComercioAsync("Barbería Norte", "barberia-norte", sellos: 3, premio: "Corte gratis");
        var lucia = await ClienteAsync();
        await Handler().Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default);
        await Handler().Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default);

        var r = await Handler().Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default);

        Assert.True(r.Valor.TarjetaCompleta);
        var aviso = Assert.Single(Notificador.Enviados);
        Assert.Equal(TipoAviso.TarjetaCompleta, aviso.Tipo);
        Assert.Equal("¡Completaste tu tarjeta en Barbería Norte! Ya podés canjear: Corte gratis.", aviso.Mensaje);
        var guardado = await NuevoContexto().Avisos.SingleAsync();
        Assert.Equal(TipoAviso.TarjetaCompleta, guardado.Tipo);
        Assert.Equal(lucia.Id, guardado.ClienteId);
    }

    [Fact]
    public async Task Con_la_tarjeta_completa_suma_igual_y_no_vuelve_a_avisar()
    {
        var barberia = await ComercioAsync(sellos: 3);
        var lucia = await ClienteAsync();
        for (var i = 0; i < 3; i++) await Handler().Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default);

        var r = await Handler().Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default);

        Assert.True(r.Exito);
        Assert.Equal(4, r.Valor.SellosActuales);
        Assert.True(r.Valor.TarjetaCompleta);
        Assert.Equal(4, (await NuevoContexto().Tarjetas.SingleAsync()).SellosActuales);
        // Avisó una sola vez: cuando la tarjeta se completó, no con el sello que sobra.
        Assert.Single(Notificador.Enviados);
    }

    [Fact]
    public async Task Respeta_el_tiempo_minimo_entre_sellos()
    {
        var cafe = await ComercioAsync(horasEntreSellos: 1);
        var lucia = await ClienteAsync();
        await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);

        Reloj.Avanzar(TimeSpan.FromMinutes(30));
        var antes = await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);
        Reloj.Avanzar(TimeSpan.FromMinutes(30));
        var despues = await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);

        AssertError(antes, TipoError.Conflicto, "Ya se sumó un sello hace menos de 1 h. Si es una compra nueva, esperá o cambiá la regla del programa.");
        Assert.True(despues.Exito);
        Assert.Equal(2, despues.Valor.SellosActuales);
    }

    [Fact]
    public async Task Cliente_o_comercio_inexistente_devuelve_no_encontrado()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        AssertError(await Handler().Handle(new SumarSello(cafe.Id, 999, "caja"), default), TipoError.NoEncontrado, "El cliente no existe.");
        AssertError(await Handler().Handle(new SumarSello(999, lucia.Id, "caja"), default), TipoError.NoEncontrado,
            "El comercio no existe o no tiene tarjeta configurada.");
    }
}

public class CanjeTests : BaseDePrueba
{
    async Task<(Comercio Barberia, Cliente Lucia, int TarjetaId)> TarjetaCompletaAsync()
    {
        var barberia = await ComercioAsync("Barbería Norte", "barberia-norte", sellos: 3, diasParaRetirar: 7, premio: "Corte gratis");
        var lucia = await ClienteAsync();
        var sumar = new SumarSelloHandler(Db, Notificador, Reloj);
        SelloSumado? ultimo = null;
        for (var i = 0; i < 3; i++) ultimo = (await sumar.Handle(new SumarSello(barberia.Id, lucia.Id, "caja"), default)).Valor;
        return (barberia, lucia, ultimo!.TarjetaId);
    }

    CanjearPremioHandler Canjear() => new(Db, Reloj);
    EntregarCanjeHandler Entregar() => new(Db, Reloj);

    [Fact]
    public async Task Canjear_y_entregar_el_premio()
    {
        var (barberia, lucia, tarjetaId) = await TarjetaCompletaAsync();

        var canje = await Canjear().Handle(new CanjearPremio(lucia.Id, tarjetaId), default);
        Reloj.Avanzar(TimeSpan.FromDays(1));
        var entrega = await Entregar().Handle(new EntregarCanje(barberia.Id, " " + canje.Valor.Codigo.ToLowerInvariant() + " ", "Nico (dueño)"), default);

        Assert.True(canje.Exito);
        Assert.Equal("Corte gratis", canje.Valor.Premio);
        Assert.Equal(Inicio.AddDays(7), canje.Valor.VenceEn);
        Assert.Equal(6, canje.Valor.Codigo.Length);
        Assert.True(entrega.Exito);
        Assert.Equal("Lucía Fernández", entrega.Valor.Cliente);
        var ctx = NuevoContexto();
        var tarjeta = await ctx.Tarjetas.SingleAsync();
        Assert.Equal(0, tarjeta.SellosActuales);
        Assert.Equal(3, tarjeta.SellosTotales);
        var guardado = await ctx.Canjes.SingleAsync();
        Assert.Equal("Nico (dueño)", guardado.EntregadoPor);
        Assert.NotNull(guardado.EntregadoEn);
    }

    [Fact]
    public async Task Canjear_sin_los_sellos_necesarios_devuelve_conflicto()
    {
        var cafe = await ComercioAsync(sellos: 10);
        var lucia = await ClienteAsync();
        var sello = await new SumarSelloHandler(Db, Notificador, Reloj).Handle(new SumarSello(cafe.Id, lucia.Id, "caja"), default);

        var r = await Canjear().Handle(new CanjearPremio(lucia.Id, sello.Valor.TarjetaId), default);

        AssertError(r, TipoError.Conflicto, "Todavía te faltan 9 sellos.");
        Assert.Equal(0, await NuevoContexto().Canjes.CountAsync());
    }

    [Fact]
    public async Task Un_cliente_no_puede_canjear_la_tarjeta_de_otro()
    {
        var (_, _, tarjetaId) = await TarjetaCompletaAsync();
        var ana = await ClienteAsync("Ana Torres", "35123456", "1166667777");

        var r = await Canjear().Handle(new CanjearPremio(ana.Id, tarjetaId), default);

        AssertError(r, TipoError.NoEncontrado, "Esa tarjeta no está en tu billetera.");
    }

    [Fact]
    public async Task Otro_comercio_no_puede_entregar_el_canje()
    {
        var (_, lucia, tarjetaId) = await TarjetaCompletaAsync();
        var cafe = await ComercioAsync("Café Tostado", "cafe-tostado");
        var canje = await Canjear().Handle(new CanjearPremio(lucia.Id, tarjetaId), default);

        var r = await Entregar().Handle(new EntregarCanje(cafe.Id, canje.Valor.Codigo, "caja café"), default);

        AssertError(r, TipoError.NoEncontrado, "Ese código no existe en este comercio.");
        Assert.Null((await NuevoContexto().Canjes.SingleAsync()).EntregadoEn);
    }

    [Fact]
    public async Task No_se_entrega_dos_veces()
    {
        var (barberia, lucia, tarjetaId) = await TarjetaCompletaAsync();
        var canje = await Canjear().Handle(new CanjearPremio(lucia.Id, tarjetaId), default);
        Assert.True((await Entregar().Handle(new EntregarCanje(barberia.Id, canje.Valor.Codigo, "caja"), default)).Exito);

        var r = await Entregar().Handle(new EntregarCanje(barberia.Id, canje.Valor.Codigo, "caja"), default);

        AssertError(r, TipoError.Conflicto);
        Assert.StartsWith("Ese premio ya se entregó el ", r.Error!.Mensaje);
    }

    [Fact]
    public async Task Un_codigo_vencido_no_se_entrega()
    {
        var (barberia, lucia, tarjetaId) = await TarjetaCompletaAsync();
        var canje = await Canjear().Handle(new CanjearPremio(lucia.Id, tarjetaId), default);
        Reloj.Avanzar(TimeSpan.FromDays(8));

        var r = await Entregar().Handle(new EntregarCanje(barberia.Id, canje.Valor.Codigo, "caja"), default);

        AssertError(r, TipoError.Conflicto);
        Assert.StartsWith("El código venció el ", r.Error!.Mensaje);
    }

    [Fact]
    public async Task Un_codigo_inexistente_devuelve_no_encontrado()
    {
        var barberia = await ComercioAsync();

        var r = await Entregar().Handle(new EntregarCanje(barberia.Id, "ZZZZZZ", "caja"), default);

        AssertError(r, TipoError.NoEncontrado, "Ese código no existe en este comercio.");
    }
}

public class ClientesEnRiesgoTests : BaseDePrueba
{
    [Fact]
    public async Task Lista_solo_a_los_que_se_enfrian_y_primero_los_mas_urgentes()
    {
        var cafe = await ComercioAsync();
        var otro = await ComercioAsync("Barbería Norte", "barberia-norte");
        var alDia = await ClienteAsync("Carla Díaz", "31000001", "1100000001");
        var riesgo = await ClienteAsync("Martín Gómez", "31000002", "1100000002");
        var critico = await ClienteAsync("Sofía López", "31000003", "1100000003");
        var deOtroComercio = await ClienteAsync("Diego Ruiz", "31000004", "1100000004");
        await TarjetaConHistorialAsync(alDia, cafe, visitas: 6, cadaDias: 4, ultimaHaceDias: 3);     // 0,75x
        await TarjetaConHistorialAsync(riesgo, cafe, visitas: 6, cadaDias: 5, ultimaHaceDias: 12);   // 2,4x
        await TarjetaConHistorialAsync(critico, cafe, visitas: 6, cadaDias: 3, ultimaHaceDias: 30);  // 10x
        await TarjetaConHistorialAsync(deOtroComercio, otro, visitas: 6, cadaDias: 3, ultimaHaceDias: 60);

        var lista = await new ClientesEnRiesgoHandler(Db, Reloj).Handle(new ClientesEnRiesgo(cafe.Id), default);

        Assert.Equal(new[] { "Sofía López", "Martín Gómez" }, lista.Select(c => c.Nombre).ToArray());
        Assert.Equal(EstadoCliente.Critico, lista[0].Estado);
        Assert.Equal(EstadoCliente.EnRiesgo, lista[1].Estado);
        Assert.Equal(30, lista[0].DiasSinVenir);
        Assert.Equal((double?)3, lista[0].CadaCuantosDias);
        Assert.Equal("1100000003", lista[0].Telefono);
    }

    [Fact]
    public async Task Sin_tarjetas_devuelve_una_lista_vacia()
    {
        var cafe = await ComercioAsync();
        Assert.Empty(await new ClientesEnRiesgoHandler(Db, Reloj).Handle(new ClientesEnRiesgo(cafe.Id), default));
    }
}

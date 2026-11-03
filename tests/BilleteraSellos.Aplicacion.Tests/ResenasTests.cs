using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;
using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

namespace BilleteraSellos.Aplicacion.Tests;

/// <summary>Pedido automático de reseña en Google: se envía una sola vez, en la visita que elige el comercio.</summary>
public class ResenasTests : BaseDePrueba
{
    const string Link = "https://g.page/r/CafeTostado/review";

    SumarSelloHandler Sumar() => new(Db, Notificador, Reloj);
    ObtenerBilleteraHandler Billetera() => new(Db, Reloj);
    ResponderResenaHandler Responder() => new(Db, Reloj);

    async Task<Comercio> CafeConResenaEnVisitaAsync(int visita)
    {
        var cafe = await ComercioAsync();
        Assert.True(cafe.ConfigurarResenas(Link, visita).Exito);
        await Db.SaveChangesAsync();
        return cafe;
    }

    async Task SumarVecesAsync(Comercio comercio, Cliente cliente, int veces)
    {
        for (var i = 0; i < veces; i++)
            Assert.True((await Sumar().Handle(new SumarSello(comercio.Id, cliente.Id, "caja"), default)).Exito);
    }

    List<(int ClienteId, int ComercioId, TipoAviso Tipo, string Mensaje)> PedidosDeResena() =>
        Notificador.Enviados.Where(a => a.Tipo == TipoAviso.Resena).ToList();

    [Fact]
    public async Task En_la_visita_elegida_se_pide_la_resena_con_el_link()
    {
        var cafe = await CafeConResenaEnVisitaAsync(3);
        var lucia = await ClienteAsync();

        await SumarVecesAsync(cafe, lucia, 2);
        Assert.Empty(PedidosDeResena());

        await SumarVecesAsync(cafe, lucia, 1);

        var pedido = Assert.Single(PedidosDeResena());
        Assert.Equal(lucia.Id, pedido.ClienteId);
        Assert.Equal($"¿Te gustó Café Tostado? Contanos en Google: {Link}", pedido.Mensaje);
        var guardado = await NuevoContexto().Avisos.SingleAsync(a => a.Tipo == TipoAviso.Resena);
        Assert.Equal(pedido.Mensaje, guardado.Mensaje);
        var tarjeta = await NuevoContexto().Tarjetas.SingleAsync();
        Assert.Equal(Inicio, tarjeta.ResenaPedidaEn);
    }

    [Fact]
    public async Task No_se_vuelve_a_pedir_en_las_visitas_siguientes()
    {
        var cafe = await CafeConResenaEnVisitaAsync(2);
        var lucia = await ClienteAsync();

        await SumarVecesAsync(cafe, lucia, 6);

        Assert.Single(PedidosDeResena());
    }

    [Fact]
    public async Task Cuenta_las_visitas_anteriores_de_la_tarjeta()
    {
        var cafe = await CafeConResenaEnVisitaAsync(5);
        var lucia = await ClienteAsync();
        await TarjetaConHistorialAsync(lucia, cafe, visitas: 4, cadaDias: 3, ultimaHaceDias: 1);

        await SumarVecesAsync(cafe, lucia, 1);

        Assert.Single(PedidosDeResena());
    }

    [Fact]
    public async Task Si_ya_paso_la_visita_cuando_se_activo_no_se_pide()
    {
        var cafe = await CafeConResenaEnVisitaAsync(3);
        var lucia = await ClienteAsync();
        await TarjetaConHistorialAsync(lucia, cafe, visitas: 5, cadaDias: 3, ultimaHaceDias: 1);

        await SumarVecesAsync(cafe, lucia, 2);

        Assert.Empty(PedidosDeResena());
    }

    [Fact]
    public async Task Si_el_cliente_no_acepta_promos_no_se_pide()
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var lucia = await ClienteAsync();
        await SumarVecesAsync(cafe, lucia, 1);
        Assert.Single(PedidosDeResena());

        var pepe = await ClienteAsync("Pepe Paz", "31222333", "1166667777");
        var tarjeta = Tarjeta.Abrir(pepe.Id, cafe.Id, Inicio);
        tarjeta.CambiarPromos(false);
        Db.Tarjetas.Add(tarjeta);
        await Db.SaveChangesAsync();
        await SumarVecesAsync(cafe, pepe, 1);

        Assert.Single(PedidosDeResena());
    }

    [Fact]
    public async Task Sin_configurar_nunca_se_pide()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        await SumarVecesAsync(cafe, lucia, 5);

        Assert.Empty(PedidosDeResena());
    }

    [Fact]
    public async Task Tambien_se_pide_al_usar_el_QR_de_la_compra()
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var cajero = await UsuarioAsync(cafe, rol: RolUsuario.Cajero);
        var lucia = await ClienteAsync();
        var qr = await new GenerarQrCompraHandler(Db, Reloj).Handle(new GenerarQrCompra(cafe.Id, cajero.Id, null), default);

        var r = await new UsarQrCompraHandler(Db, Notificador, Reloj).Handle(new UsarQrCompra(lucia.Id, qr.Valor.Codigo), default);

        Assert.True(r.Exito);
        Assert.Single(PedidosDeResena());
    }

    [Fact]
    public async Task La_billetera_muestra_el_pedido_hasta_que_el_cliente_contesta()
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var lucia = await ClienteAsync();
        Assert.Empty((await Billetera().Handle(new ObtenerBilletera(lucia.Id), default)).Valor.Tarjetas);

        await SumarVecesAsync(cafe, lucia, 1);
        var tarjeta = Assert.Single((await Billetera().Handle(new ObtenerBilletera(lucia.Id), default)).Valor.Tarjetas);
        Assert.True(tarjeta.PedirResena);
        Assert.Equal(Link, tarjeta.ResenaGoogleUrl);

        var r = await Responder().Handle(new ResponderResena(lucia.Id, tarjeta.TarjetaId, "AhoraNo"), default);

        Assert.True(r.Exito);
        Assert.False(Assert.Single((await Billetera().Handle(new ObtenerBilletera(lucia.Id), default)).Valor.Tarjetas).PedirResena);
        var guardada = await NuevoContexto().Tarjetas.SingleAsync();
        Assert.Equal(RespuestaResena.AhoraNo, guardada.ResenaRespuesta);
    }

    [Theory]
    [InlineData("dejada")]
    [InlineData("Dejada")]
    [InlineData(" AhoraNo ")]
    public async Task Responder_acepta_las_dos_respuestas_sin_importar_mayusculas(string respuesta)
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var lucia = await ClienteAsync();
        await SumarVecesAsync(cafe, lucia, 1);
        var tarjeta = await Db.Tarjetas.SingleAsync();

        Assert.True((await Responder().Handle(new ResponderResena(lucia.Id, tarjeta.Id, respuesta), default)).Exito);
    }

    [Theory]
    [InlineData("Quizas")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("5")]
    public async Task Responder_con_otra_cosa_devuelve_error_de_validacion(string? respuesta)
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var lucia = await ClienteAsync();
        await SumarVecesAsync(cafe, lucia, 1);
        var tarjeta = await Db.Tarjetas.SingleAsync();

        AssertError(await Responder().Handle(new ResponderResena(lucia.Id, tarjeta.Id, respuesta), default),
            TipoError.Validacion, "La respuesta tiene que ser Dejada o AhoraNo.");
    }

    [Fact]
    public async Task Responder_por_la_tarjeta_de_otro_cliente_no_la_encuentra()
    {
        var cafe = await CafeConResenaEnVisitaAsync(1);
        var lucia = await ClienteAsync();
        var pepe = await ClienteAsync("Pepe Paz", "31222333", "1166667777");
        await SumarVecesAsync(cafe, lucia, 1);
        var tarjeta = await Db.Tarjetas.SingleAsync();

        AssertError(await Responder().Handle(new ResponderResena(pepe.Id, tarjeta.Id, "Dejada"), default),
            TipoError.NoEncontrado, "Esa tarjeta no está en tu billetera.");
    }

    [Fact]
    public async Task Responder_sin_que_se_le_haya_pedido_devuelve_conflicto()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();
        await SumarVecesAsync(cafe, lucia, 1);
        var tarjeta = await Db.Tarjetas.SingleAsync();

        AssertError(await Responder().Handle(new ResponderResena(lucia.Id, tarjeta.Id, "Dejada"), default),
            TipoError.Conflicto, "No te pedimos una reseña para este comercio.");
    }

    [Fact]
    public async Task El_dueno_configura_el_link_y_la_visita_en_el_programa()
    {
        var cafe = await ComercioAsync();

        var r = await new ActualizarProgramaHandler(Db).Handle(
            new ActualizarPrograma(cafe.Id, 10, "Café gratis", 900, 7, 0, null, null, Link, 4), default);

        Assert.True(r.Exito);
        Assert.Equal(Link, r.Valor.ResenaGoogleUrl);
        Assert.Equal(4, r.Valor.PedirResenaTrasVisitas);
        var leido = await new ObtenerProgramaHandler(NuevoContexto()).Handle(new ObtenerPrograma(cafe.Id), default);
        Assert.Equal(Link, leido.Valor.ResenaGoogleUrl);
        Assert.Equal(4, leido.Valor.PedirResenaTrasVisitas);
    }

    [Fact]
    public async Task Sin_mandar_el_link_se_mantiene_el_que_estaba()
    {
        var cafe = await CafeConResenaEnVisitaAsync(3);

        var r = await new ActualizarProgramaHandler(Db).Handle(new ActualizarPrograma(cafe.Id, 8, "Café gratis", 900, 7, 0), default);

        Assert.True(r.Exito);
        Assert.Equal(Link, r.Valor.ResenaGoogleUrl);
        Assert.Null(r.Valor.PedirResenaTrasVisitas);
    }

    [Fact]
    public async Task Link_invalido_no_guarda_ningun_cambio()
    {
        var cafe = await ComercioAsync();

        var r = await new ActualizarProgramaHandler(Db).Handle(
            new ActualizarPrograma(cafe.Id, 5, "Otro premio", 900, 7, 0, null, null, "https://facebook.com/cafe", 3), default);

        AssertError(r, TipoError.Validacion, "El link tiene que ser de Google (el que te da Google para pedir reseñas).");
        var programa = await NuevoContexto().Programas.SingleAsync();
        Assert.Equal(10, programa.SellosNecesarios);
        Assert.Null(programa.PedirResenaTrasVisitas);
    }

    [Fact]
    public async Task Pedir_resena_sin_link_devuelve_error()
    {
        var cafe = await ComercioAsync();

        var r = await new ActualizarProgramaHandler(Db).Handle(
            new ActualizarPrograma(cafe.Id, 10, "Café gratis", 900, 7, 0, null, null, null, 3), default);

        AssertError(r, TipoError.Validacion, "Cargá primero el link de reseñas de Google del comercio.");
    }
}

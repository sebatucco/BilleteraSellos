namespace BilleteraSellos.Dominio.Tests;

/// <summary>Pedido automático de reseña en Google: configuración del comercio y cuándo se le pide al cliente.</summary>
public class ResenasTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);
    const string Link = "https://g.page/r/CafeTostado/review";

    static Comercio Cafe(string? link = null)
    {
        var p = ProgramaSellos.Nuevo(10, "Café gratis", 900);
        return Comercio.Crear("Café Tostado", "Cafetería", "Av. Rivadavia 4120", "cafe-tostado", null, p, link);
    }

    [Theory]
    [InlineData("https://g.page/r/CafeTostado/review")]
    [InlineData("https://search.google.com/local/writereview?placeid=ChIJ123")]
    [InlineData("https://www.google.com/maps/place/Cafe")]
    [InlineData("https://maps.app.goo.gl/AbC123")]
    [InlineData("https://goo.gl/maps/xyz")]
    [InlineData("https://www.google.com.ar/maps/place/Cafe")]
    [InlineData("  https://g.page/r/CafeTostado/review  ")]
    public void Link_de_Google_es_valido(string link) => Assert.True(ResenasGoogle.EsLinkValido(link));

    [Theory]
    [InlineData("http://g.page/r/CafeTostado/review")]
    [InlineData("https://facebook.com/cafetostado")]
    [InlineData("https://google.com.evil.com/review")]
    [InlineData("https://notgoogle.com/review")]
    [InlineData("g.page/r/CafeTostado/review")]
    [InlineData("cualquier cosa")]
    [InlineData("")]
    [InlineData(null)]
    public void Link_que_no_es_de_Google_no_es_valido(string? link) => Assert.False(ResenasGoogle.EsLinkValido(link));

    [Fact]
    public void Configurar_link_y_visita()
    {
        var c = Cafe();

        Assert.True(c.ConfigurarResenas(Link, 3).Exito);

        Assert.Equal(Link, c.ResenaGoogleUrl);
        Assert.Equal(3, c.Programa!.PedirResenaTrasVisitas);
    }

    [Fact]
    public void Link_invalido_devuelve_error_y_no_cambia_nada()
    {
        var c = Cafe(Link);
        Assert.True(c.ConfigurarResenas(null, 3).Exito);

        var r = c.ConfigurarResenas("https://facebook.com/cafe", 5);

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal("El link tiene que ser de Google (el que te da Google para pedir reseñas).", r.Error.Mensaje);
        Assert.Equal(Link, c.ResenaGoogleUrl);
        Assert.Equal(3, c.Programa!.PedirResenaTrasVisitas);
    }

    [Fact]
    public void Link_demasiado_largo_no_se_acepta()
    {
        var largo = "https://g.page/r/" + new string('a', Comercio.MaxLargoLinkResena);
        Assert.False(Cafe().ConfigurarResenas(largo, null).Exito);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Visita_fuera_de_rango_devuelve_error(int visita)
    {
        var r = Cafe(Link).ConfigurarResenas(null, visita);

        Assert.False(r.Exito);
        Assert.Equal("El pedido de reseña tiene que ser entre la visita 1 y la 100.", r.Error!.Mensaje);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Visita_en_los_bordes_se_acepta(int visita) => Assert.True(Cafe(Link).ConfigurarResenas(null, visita).Exito);

    [Fact]
    public void Pedir_resena_sin_link_devuelve_error()
    {
        var r = Cafe().ConfigurarResenas(null, 3);

        Assert.False(r.Exito);
        Assert.Equal("Cargá primero el link de reseñas de Google del comercio.", r.Error!.Mensaje);
    }

    [Fact]
    public void Borrar_el_link_con_el_pedido_activo_devuelve_error()
    {
        var c = Cafe(Link);

        var r = c.ConfigurarResenas("", 3);

        Assert.False(r.Exito);
        Assert.Equal("Cargá primero el link de reseñas de Google del comercio.", r.Error!.Mensaje);
        Assert.Equal(Link, c.ResenaGoogleUrl);
    }

    [Fact]
    public void Link_null_no_cambia_y_vacio_lo_borra()
    {
        var c = Cafe(Link);

        Assert.True(c.ConfigurarResenas(null, null).Exito);
        Assert.Equal(Link, c.ResenaGoogleUrl);

        Assert.True(c.ConfigurarResenas("   ", null).Exito);
        Assert.Null(c.ResenaGoogleUrl);
        Assert.Null(c.Programa!.PedirResenaTrasVisitas);
    }

    static (Tarjeta Tarjeta, ProgramaSellos Programa) TarjetaConPedidoEnVisita(int visita)
    {
        var c = Cafe(Link);
        Assert.True(c.ConfigurarResenas(null, visita).Exito);
        return (Tarjeta.Abrir(1, 1, Ahora), c.Programa!);
    }

    [Fact]
    public void Corresponde_pedir_solo_en_la_visita_exacta()
    {
        var (t, p) = TarjetaConPedidoEnVisita(3);

        Assert.False(t.CorrespondePedirResena(2, p, Link));
        Assert.True(t.CorrespondePedirResena(3, p, Link));
        Assert.False(t.CorrespondePedirResena(4, p, Link));
    }

    [Fact]
    public void No_corresponde_pedir_sin_link_sin_configurar_o_si_no_acepta_promos()
    {
        var (t, p) = TarjetaConPedidoEnVisita(3);
        Assert.False(t.CorrespondePedirResena(3, p, null));
        Assert.False(t.CorrespondePedirResena(3, ProgramaSellos.Nuevo(10, "Café", 900), Link));

        t.CambiarPromos(false);
        Assert.False(t.CorrespondePedirResena(3, p, Link));
    }

    [Fact]
    public void Se_pide_una_sola_vez()
    {
        var (t, p) = TarjetaConPedidoEnVisita(3);

        t.MarcarResenaPedida(Ahora);
        t.MarcarResenaPedida(Ahora.AddDays(5));

        Assert.Equal(Ahora, t.ResenaPedidaEn);
        Assert.False(t.CorrespondePedirResena(3, p, Link));
        Assert.True(t.ResenaPendiente);
    }

    [Fact]
    public void Responder_sin_pedido_devuelve_conflicto()
    {
        var r = Tarjeta.Abrir(1, 1, Ahora).ResponderResena(RespuestaResena.Dejada, Ahora);

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Conflicto, r.Error!.Tipo);
        Assert.Equal("No te pedimos una reseña para este comercio.", r.Error.Mensaje);
    }

    [Theory]
    [InlineData(RespuestaResena.Dejada)]
    [InlineData(RespuestaResena.AhoraNo)]
    public void Responder_guarda_la_respuesta_y_deja_de_estar_pendiente(RespuestaResena respuesta)
    {
        var t = Tarjeta.Abrir(1, 1, Ahora);
        t.MarcarResenaPedida(Ahora);

        Assert.True(t.ResponderResena(respuesta, Ahora.AddHours(2)).Exito);

        Assert.False(t.ResenaPendiente);
        Assert.Equal(respuesta, t.ResenaRespuesta);
        Assert.Equal(Ahora.AddHours(2), t.ResenaRespondidaEn);
    }
}

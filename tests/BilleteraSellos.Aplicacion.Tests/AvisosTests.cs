using BilleteraSellos.Aplicacion.Funcionalidades.Avisos;
using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

namespace BilleteraSellos.Aplicacion.Tests;

public class ProcesarAvisosTests : BaseDePrueba
{
    ProcesarAvisosHandler Handler() => new(Db, Notificador, Reloj);

    [Fact]
    public async Task Saluda_a_quien_cumple_anios_hoy_en_Argentina()
    {
        var cafe = await ComercioAsync();
        // Inicio es 15/10 a las 12:00 en Argentina.
        var sofia = await ClienteAsync("Sofía López", "33777888", "1162223333", new DateOnly(1999, 10, 15));
        var otro = await ClienteAsync("Martín Gómez", "28444555", "1160001111", new DateOnly(1995, 10, 16));
        await TarjetaConHistorialAsync(sofia, cafe, visitas: 3, cadaDias: 5, ultimaHaceDias: 1);
        await TarjetaConHistorialAsync(otro, cafe, visitas: 3, cadaDias: 5, ultimaHaceDias: 1);

        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(1, r.Cumpleanos);
        var aviso = Assert.Single(Notificador.Enviados);
        Assert.Equal(sofia.Id, aviso.ClienteId);
        Assert.Equal(TipoAviso.Cumpleanos, aviso.Tipo);
        Assert.Equal("¡Feliz cumpleaños, Sofía! Te saludamos desde Café Tostado.", aviso.Mensaje);
    }

    [Fact]
    public async Task El_cumpleanos_se_saluda_una_sola_vez()
    {
        var cafe = await ComercioAsync();
        var sofia = await ClienteAsync("Sofía López", "33777888", "1162223333", new DateOnly(1999, 10, 15));
        await TarjetaConHistorialAsync(sofia, cafe, visitas: 3, cadaDias: 5, ultimaHaceDias: 1);
        await Handler().Handle(new ProcesarAvisos(), default);

        Reloj.Avanzar(TimeSpan.FromHours(2));
        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(0, r.Cumpleanos);
        Assert.Single(Notificador.Enviados);
    }

    [Fact]
    public async Task Invita_a_volver_a_quien_se_enfria_con_los_sellos_que_le_faltan()
    {
        var cafe = await ComercioAsync(sellos: 10);
        var martin = await ClienteAsync("Martín Gómez", "28444555", "1160001111");
        var carla = await ClienteAsync("Carla Díaz", "31000001", "1100000001");
        await TarjetaConHistorialAsync(martin, cafe, visitas: 9, cadaDias: 5, ultimaHaceDias: 24); // 9 sellos: le falta 1
        await TarjetaConHistorialAsync(carla, cafe, visitas: 6, cadaDias: 4, ultimaHaceDias: 2);   // al día

        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(1, r.Reactivaciones);
        var aviso = Assert.Single(Notificador.Enviados);
        Assert.Equal(martin.Id, aviso.ClienteId);
        Assert.Equal(TipoAviso.Reactivacion, aviso.Tipo);
        Assert.Equal("Te extrañamos en Café Tostado. Te falta 1 sello para tu premio.", aviso.Mensaje);
        Assert.Equal(1, await NuevoContexto().Avisos.CountAsync(a => a.Tipo == TipoAviso.Reactivacion));
    }

    [Fact]
    public async Task No_repite_la_invitacion_antes_de_14_dias()
    {
        var cafe = await ComercioAsync();
        var martin = await ClienteAsync("Martín Gómez", "28444555", "1160001111");
        await TarjetaConHistorialAsync(martin, cafe, visitas: 5, cadaDias: 5, ultimaHaceDias: 24);
        await Handler().Handle(new ProcesarAvisos(), default);

        Reloj.Avanzar(TimeSpan.FromDays(13));
        var antes = await Handler().Handle(new ProcesarAvisos(), default);
        Reloj.Avanzar(TimeSpan.FromDays(2));
        var despues = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(0, antes.Reactivaciones);
        Assert.Equal(1, despues.Reactivaciones);
        Assert.Equal(2, Notificador.Enviados.Count);
    }

    [Fact]
    public async Task Respeta_a_quien_no_acepta_promos_del_comercio()
    {
        var cafe = await ComercioAsync();
        var sofia = await ClienteAsync("Sofía López", "33777888", "1162223333", new DateOnly(1999, 10, 15));
        var t = await TarjetaConHistorialAsync(sofia, cafe, visitas: 5, cadaDias: 3, ultimaHaceDias: 40);
        var cambio = await new CambiarPromosHandler(Db).Handle(new CambiarPromos(sofia.Id, t.Id, false), default);
        Assert.True(cambio.Exito);

        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(new ResultadoAvisos(0, 0), r);
        Assert.Empty(Notificador.Enviados);
    }

    [Fact]
    public async Task El_saludo_de_cumpleanos_reemplaza_a_la_invitacion_ese_dia()
    {
        var cafe = await ComercioAsync();
        var sofia = await ClienteAsync("Sofía López", "33777888", "1162223333", new DateOnly(1999, 10, 15));
        await TarjetaConHistorialAsync(sofia, cafe, visitas: 5, cadaDias: 3, ultimaHaceDias: 40); // además se está enfriando

        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(new ResultadoAvisos(1, 0), r);
        Assert.Equal(TipoAviso.Cumpleanos, Assert.Single(Notificador.Enviados).Tipo);
    }

    [Fact]
    public async Task Cada_comercio_avisa_por_su_cuenta()
    {
        var cafe = await ComercioAsync();
        var barberia = await ComercioAsync("Barbería Norte", "barberia-norte");
        var martin = await ClienteAsync("Martín Gómez", "28444555", "1160001111");
        await TarjetaConHistorialAsync(martin, cafe, visitas: 5, cadaDias: 5, ultimaHaceDias: 24);
        await TarjetaConHistorialAsync(martin, barberia, visitas: 5, cadaDias: 20, ultimaHaceDias: 70);

        var r = await Handler().Handle(new ProcesarAvisos(), default);

        Assert.Equal(2, r.Reactivaciones);
        Assert.Equal(new[] { cafe.Id, barberia.Id }.OrderBy(x => x), Notificador.Enviados.Select(a => a.ComercioId).OrderBy(x => x));
    }
}

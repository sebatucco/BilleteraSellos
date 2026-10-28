using BilleteraSellos.Aplicacion.Funcionalidades.Acceso;
using BilleteraSellos.Aplicacion.Funcionalidades.Equipo;
using Microsoft.Extensions.Logging.Abstractions;

namespace BilleteraSellos.Aplicacion.Tests;

public class CambiarEstadoUsuarioTests : BaseDePrueba
{
    CambiarEstadoUsuarioHandler Handler() => new(Db, Sesiones);

    [Fact]
    public async Task Desactivar_un_cajero_cierra_todas_sus_sesiones()
    {
        var cafe = await ComercioAsync();
        var duena = await UsuarioAsync(cafe);
        var cajero = await UsuarioAsync(cafe, "caja@cafe.demo", rol: RolUsuario.Cajero, nombre: "Juan (caja)");
        var (celular, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: cajero.Id);
        var (computadora, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: cajero.Id);
        var (deLaDuena, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: duena.Id);

        var r = await Handler().Handle(new CambiarEstadoUsuario(cafe.Id, cajero.Id, false, duena.Id), default);

        Assert.True(r.Exito);
        Assert.False(r.Valor.Activo);
        Assert.Equal(2, await NuevoContexto().Sesiones.CountAsync(s => s.UsuarioId == cajero.Id && s.RevocadaEn != null));
        Assert.Null(await Sesiones.ValidarAsync(celular));
        Assert.Null(await Sesiones.ValidarAsync(computadora));
        Assert.NotNull(await Sesiones.ValidarAsync(deLaDuena));
    }

    [Fact]
    public async Task Reactivar_vuelve_a_dejarlo_entrar()
    {
        var cafe = await ComercioAsync();
        var duena = await UsuarioAsync(cafe);
        var cajero = await UsuarioAsync(cafe, "caja@cafe.demo", rol: RolUsuario.Cajero);
        await Handler().Handle(new CambiarEstadoUsuario(cafe.Id, cajero.Id, false, duena.Id), default);

        var r = await Handler().Handle(new CambiarEstadoUsuario(cafe.Id, cajero.Id, true, duena.Id), default);
        var login = await new LoginComercioHandler(Db, Hasher, Sesiones, Reloj).Handle(new LoginComercio("caja@cafe.demo", "Demo1234"), default);

        Assert.True(r.Valor.Activo);
        Assert.True(login.Exito);
    }

    [Fact]
    public async Task Nadie_puede_desactivarse_a_si_mismo()
    {
        var cafe = await ComercioAsync();
        var duena = await UsuarioAsync(cafe);

        var r = await Handler().Handle(new CambiarEstadoUsuario(cafe.Id, duena.Id, false, duena.Id), default);

        AssertError(r, TipoError.Validacion, "No podés desactivarte a vos mismo.");
        Assert.True((await NuevoContexto().Usuarios.SingleAsync()).Activo);
    }

    [Fact]
    public async Task No_se_puede_tocar_a_un_usuario_de_otro_comercio()
    {
        var cafe = await ComercioAsync();
        var barberia = await ComercioAsync("Barbería Norte", "barberia-norte");
        var duenaCafe = await UsuarioAsync(cafe);
        var cajeroBarberia = await UsuarioAsync(barberia, "caja@barberia.demo", rol: RolUsuario.Cajero);

        var r = await Handler().Handle(new CambiarEstadoUsuario(cafe.Id, cajeroBarberia.Id, false, duenaCafe.Id), default);

        AssertError(r, TipoError.NoEncontrado, "Ese usuario no es de tu comercio.");
    }
}

public class AgregarCajeroTests : BaseDePrueba
{
    [Fact]
    public async Task Agrega_un_cajero_activo_al_comercio()
    {
        var cafe = await ComercioAsync();

        var r = await new AgregarCajeroHandler(Db, Hasher).Handle(new AgregarCajero(cafe.Id, "Pepe", "PEPE@cafe.demo", "Pepe2026"), default);

        Assert.True(r.Exito);
        Assert.Equal(RolUsuario.Cajero, r.Valor.Rol);
        Assert.Equal("pepe@cafe.demo", r.Valor.Email);
        var u = await NuevoContexto().Usuarios.SingleAsync();
        Assert.Equal(cafe.Id, u.ComercioId);
        Assert.True(Hasher.Verificar("Pepe2026", u.PasswordHash));
    }

    [Fact]
    public async Task Email_repetido_o_contrasena_debil_no_agregan()
    {
        var cafe = await ComercioAsync();
        await UsuarioAsync(cafe, "pepe@cafe.demo");
        var h = new AgregarCajeroHandler(Db, Hasher);

        AssertError(await h.Handle(new AgregarCajero(cafe.Id, "Pepe", "pepe@cafe.demo", "Pepe2026"), default), TipoError.Conflicto);
        AssertError(await h.Handle(new AgregarCajero(cafe.Id, "Otro", "otro@cafe.demo", "123"), default), TipoError.Validacion,
            "La contraseña tiene que tener al menos 8 caracteres.");
        Assert.Equal(1, await NuevoContexto().Usuarios.CountAsync());
    }
}

public class CambiarContrasenaTests : BaseDePrueba
{
    CambiarContrasenaHandler Handler() => new(Db, Hasher, Sesiones);

    [Fact]
    public async Task Cierra_las_otras_sesiones_y_deja_abierta_la_propia()
    {
        var u = await UsuarioAsync(await ComercioAsync());
        var (actual, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);
        var (otra, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);
        var sesionActual = await Sesiones.ValidarAsync(actual);

        var r = await Handler().Handle(new CambiarContrasena(u.Id, sesionActual!.SesionId, "Demo1234", "Nueva2026"), default);

        Assert.True(r.Exito);
        Assert.Equal("Listo, cambiaste tu contraseña. Cerramos tus sesiones en otros dispositivos.", r.Valor.Mensaje);
        Assert.NotNull(await Sesiones.ValidarAsync(actual));
        Assert.Null(await Sesiones.ValidarAsync(otra));
        Assert.True(Hasher.Verificar("Nueva2026", (await NuevoContexto().Usuarios.SingleAsync()).PasswordHash));
    }

    [Fact]
    public async Task Con_la_contrasena_actual_incorrecta_no_cambia_nada()
    {
        var u = await UsuarioAsync(await ComercioAsync());
        var (otra, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);

        var r = await Handler().Handle(new CambiarContrasena(u.Id, null, "mala1234", "Nueva2026"), default);

        AssertError(r, TipoError.Validacion, "La contraseña actual no es correcta.");
        Assert.NotNull(await Sesiones.ValidarAsync(otra));
        Assert.True(Hasher.Verificar("Demo1234", (await NuevoContexto().Usuarios.SingleAsync()).PasswordHash));
    }

    [Theory]
    [InlineData("Demo1234", "La contraseña nueva tiene que ser distinta de la actual.")]
    [InlineData("corta", "La contraseña tiene que tener al menos 8 caracteres.")]
    [InlineData("soloLetrasAca", "La contraseña tiene que tener letras y números.")]
    public async Task La_contrasena_nueva_tiene_que_ser_valida_y_distinta(string nueva, string mensaje)
    {
        var u = await UsuarioAsync(await ComercioAsync());

        var r = await Handler().Handle(new CambiarContrasena(u.Id, null, "Demo1234", nueva), default);

        AssertError(r, TipoError.Validacion, mensaje);
    }
}

public class RecuperarContrasenaTests : BaseDePrueba
{
    OlvideContrasenaHandler Olvide() => new(Db, Correo, Opciones, Reloj, NullLogger<OlvideContrasenaHandler>.Instance);
    RestablecerContrasenaHandler Restablecer() => new(Db, Hasher, Sesiones, Reloj);

    [Fact]
    public async Task Responde_lo_mismo_exista_o_no_el_email_y_solo_manda_si_existe()
    {
        await UsuarioAsync(await ComercioAsync());

        var existe = await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);
        var noExiste = await Olvide().Handle(new OlvideContrasena("nadie@demo.com"), default);

        Assert.Equal(existe.Valor.Mensaje, noExiste.Valor.Mensaje);
        Assert.Equal(OlvideContrasenaHandler.Respuesta, existe.Valor.Mensaje);
        var email = Assert.Single(Correo.Enviados);
        Assert.Equal("cafe@demo.com", email.Para);
        Assert.Contains("http://localhost:5225/#restablecer=", email.Cuerpo);
        Assert.Contains("Café Tostado", email.Cuerpo);
    }

    [Fact]
    public async Task Con_el_link_crea_la_contrasena_desbloquea_y_cierra_todas_las_sesiones()
    {
        var u = await UsuarioAsync(await ComercioAsync());
        var (sesion, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);
        var login = new LoginComercioHandler(Db, Hasher, Sesiones, Reloj);
        for (var i = 0; i < 5; i++) await login.Handle(new LoginComercio("cafe@demo.com", "mala1234"), default);
        await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);

        var r = await Restablecer().Handle(new RestablecerContrasena(Correo.TokenDelUltimoLink(), "Nueva2026"), default);

        Assert.True(r.Exito);
        Assert.Equal("Listo, ya podés ingresar con tu contraseña nueva.", r.Valor.Mensaje);
        Assert.Null(await Sesiones.ValidarAsync(sesion));
        Assert.True((await login.Handle(new LoginComercio("cafe@demo.com", "Nueva2026"), default)).Exito);
    }

    [Fact]
    public async Task El_link_sirve_una_sola_vez()
    {
        await UsuarioAsync(await ComercioAsync());
        await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);
        var token = Correo.TokenDelUltimoLink();
        Assert.True((await Restablecer().Handle(new RestablecerContrasena(token, "Nueva2026"), default)).Exito);

        var r = await Restablecer().Handle(new RestablecerContrasena(token, "Otra2026"), default);

        AssertError(r, TipoError.Validacion, "El link no es válido o ya venció. Pedí uno nuevo desde \"¿Olvidaste tu contraseña?\".");
    }

    [Fact]
    public async Task El_link_vence_a_la_hora()
    {
        await UsuarioAsync(await ComercioAsync());
        await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);
        Reloj.Avanzar(TimeSpan.FromHours(1));

        var r = await Restablecer().Handle(new RestablecerContrasena(Correo.TokenDelUltimoLink(), "Nueva2026"), default);

        AssertError(r, TipoError.Validacion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("token-inventado-que-no-existe")]
    public async Task Un_link_inventado_no_sirve(string token)
    {
        await UsuarioAsync(await ComercioAsync());

        var r = await Restablecer().Handle(new RestablecerContrasena(token, "Nueva2026"), default);

        AssertError(r, TipoError.Validacion);
    }

    [Fact]
    public async Task Un_link_nuevo_anula_el_anterior()
    {
        await UsuarioAsync(await ComercioAsync());
        await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);
        var viejo = Correo.TokenDelUltimoLink();
        await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);

        AssertError(await Restablecer().Handle(new RestablecerContrasena(viejo, "Nueva2026"), default), TipoError.Validacion);
        Assert.True((await Restablecer().Handle(new RestablecerContrasena(Correo.TokenDelUltimoLink(), "Nueva2026"), default)).Exito);
    }

    [Fact]
    public async Task Como_maximo_tres_links_por_hora()
    {
        await UsuarioAsync(await ComercioAsync());

        for (var i = 0; i < 4; i++)
            Assert.True((await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default)).Exito);

        Assert.Equal(3, Correo.Enviados.Count);
        Assert.Equal(3, await NuevoContexto().TokensRestablecimiento.CountAsync());
    }

    [Fact]
    public async Task Si_el_email_falla_responde_igual_y_no_guarda_el_link()
    {
        await UsuarioAsync(await ComercioAsync());
        Correo.Fallar = true;

        var r = await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);

        Assert.Equal(OlvideContrasenaHandler.Respuesta, r.Valor.Mensaje);
        Assert.Equal(0, await NuevoContexto().TokensRestablecimiento.CountAsync());
    }

    [Fact]
    public async Task Un_usuario_desactivado_no_recibe_link()
    {
        var u = await UsuarioAsync(await ComercioAsync());
        u.CambiarActivo(false);
        await Db.SaveChangesAsync();

        var r = await Olvide().Handle(new OlvideContrasena("cafe@demo.com"), default);

        Assert.True(r.Exito);
        Assert.Empty(Correo.Enviados);
    }
}

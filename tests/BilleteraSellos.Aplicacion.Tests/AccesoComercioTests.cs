using BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

namespace BilleteraSellos.Aplicacion.Tests;

public class LoginComercioTests : BaseDePrueba
{
    LoginComercioHandler Handler() => new(Db, Hasher, Sesiones, Reloj);

    [Fact]
    public async Task Login_correcto_devuelve_la_sesion_del_comercio()
    {
        var cafe = await ComercioAsync();
        var u = await UsuarioAsync(cafe);

        var r = await Handler().Handle(new LoginComercio(" CAFE@demo.com ", "Demo1234"), default);

        Assert.True(r.Exito);
        Assert.Equal(u.Id, r.Valor.UsuarioId);
        Assert.Equal(cafe.Id, r.Valor.ComercioId);
        Assert.Equal("Café Tostado", r.Valor.Comercio);
        Assert.Equal(RolUsuario.Dueno, r.Valor.Rol);
        Assert.Equal(Inicio.AddDays(7), r.Valor.VenceEn);

        var sesion = await Sesiones.ValidarAsync(r.Valor.Token);
        Assert.NotNull(sesion);
        Assert.Equal(TipoSesion.Comercio, sesion!.Tipo);
        Assert.Equal(cafe.Id, sesion.ComercioId);
    }

    [Fact]
    public async Task Contrasena_incorrecta_no_autoriza_y_cuenta_el_intento()
    {
        var u = await UsuarioAsync(await ComercioAsync());

        var r = await Handler().Handle(new LoginComercio("cafe@demo.com", "mala1234"), default);

        AssertError(r, TipoError.NoAutorizado, "Email o contraseña incorrectos.");
        var guardado = await NuevoContexto().Usuarios.SingleAsync(x => x.Id == u.Id);
        Assert.Equal(1, guardado.IntentosFallidos);
    }

    [Fact]
    public async Task Email_inexistente_responde_lo_mismo_que_contrasena_incorrecta()
    {
        await UsuarioAsync(await ComercioAsync());

        var r = await Handler().Handle(new LoginComercio("nadie@demo.com", "mala1234"), default);

        AssertError(r, TipoError.NoAutorizado, "Email o contraseña incorrectos.");
    }

    [Fact]
    public async Task Cinco_fallos_bloquean_aun_con_la_contrasena_correcta()
    {
        await UsuarioAsync(await ComercioAsync());
        for (var i = 0; i < 5; i++)
            await Handler().Handle(new LoginComercio("cafe@demo.com", "mala1234"), default);

        var r = await Handler().Handle(new LoginComercio("cafe@demo.com", "Demo1234"), default);

        AssertError(r, TipoError.DemasiadosIntentos, "Demasiados intentos fallidos. Probá de nuevo en 15 minutos.");
    }

    [Fact]
    public async Task El_bloqueo_se_levanta_a_los_quince_minutos()
    {
        await UsuarioAsync(await ComercioAsync());
        for (var i = 0; i < 5; i++)
            await Handler().Handle(new LoginComercio("cafe@demo.com", "mala1234"), default);

        Reloj.Avanzar(TimeSpan.FromMinutes(15));
        var r = await Handler().Handle(new LoginComercio("cafe@demo.com", "Demo1234"), default);

        Assert.True(r.Exito);
    }

    [Fact]
    public async Task Usuario_desactivado_no_puede_entrar()
    {
        var u = await UsuarioAsync(await ComercioAsync(), email: "caja@cafe.demo", rol: RolUsuario.Cajero);
        u.CambiarActivo(false);
        await Db.SaveChangesAsync();

        var r = await Handler().Handle(new LoginComercio("caja@cafe.demo", "Demo1234"), default);

        AssertError(r, TipoError.Prohibido, "Tu usuario está desactivado. Hablá con el dueño del comercio.");
    }
}

public class RegistrarComercioTests : BaseDePrueba
{
    RegistrarComercioHandler Handler() => new(Db, Hasher, Sesiones);

    [Fact]
    public async Task Crea_comercio_programa_y_dueno_y_deja_la_sesion_abierta()
    {
        var r = await Handler().Handle(new RegistrarComercio("Heladería Polo", "Heladería", "Mitre 100", "Leo", "LEO@polo.com", "Polo2026"), default);

        Assert.True(r.Exito);
        var ctx = NuevoContexto();
        var comercio = await ctx.Comercios.Include(c => c.Programa).SingleAsync();
        Assert.Equal("heladeria-polo", comercio.Codigo);
        Assert.Equal(10, comercio.Programa!.SellosNecesarios);
        var dueno = await ctx.Usuarios.SingleAsync();
        Assert.Equal("leo@polo.com", dueno.Email);
        Assert.Equal(RolUsuario.Dueno, dueno.Rol);
        Assert.Equal(comercio.Id, dueno.ComercioId);
        Assert.NotNull(await Sesiones.ValidarAsync(r.Valor.Token));
    }

    [Fact]
    public async Task Un_nombre_repetido_genera_un_codigo_de_qr_unico()
    {
        await ComercioAsync("Café Tostado", "cafe-tostado");

        var r2 = await Handler().Handle(new RegistrarComercio("Café Tostado", "", "", "Leo", "leo@nuevo.com", "Leo12345"), default);
        var r3 = await Handler().Handle(new RegistrarComercio("Cafe   Tostado!", "", "", "Ana", "ana@nuevo.com", "Ana12345"), default);

        Assert.True(r2.Exito);
        Assert.True(r3.Exito);
        var codigos = await NuevoContexto().Comercios.OrderBy(c => c.Id).Select(c => c.Codigo).ToListAsync();
        Assert.Equal(new[] { "cafe-tostado", "cafe-tostado-2", "cafe-tostado-3" }, codigos);
    }

    [Fact]
    public async Task Email_repetido_devuelve_conflicto()
    {
        await UsuarioAsync(await ComercioAsync(), email: "leo@nuevo.com");

        var r = await Handler().Handle(new RegistrarComercio("Otro", "", "", "Leo", "leo@nuevo.com", "Leo12345"), default);

        AssertError(r, TipoError.Conflicto, "Ya hay una cuenta con ese email. Iniciá sesión.");
    }

    [Theory]
    [InlineData("", "Leo", "leo@x.com", "Leo12345", "Falta el nombre del comercio o del dueño.")]
    [InlineData("Polo", "", "leo@x.com", "Leo12345", "Falta el nombre del comercio o del dueño.")]
    [InlineData("Polo", "Leo", "no-es-email", "Leo12345", "El email no es válido.")]
    [InlineData("Polo", "Leo", "leo@x.com", "corta1", "La contraseña tiene que tener al menos 8 caracteres.")]
    public async Task Datos_invalidos_devuelven_el_motivo(string comercio, string dueno, string email, string contrasena, string mensaje)
    {
        var r = await Handler().Handle(new RegistrarComercio(comercio, "", "", dueno, email, contrasena), default);

        AssertError(r, TipoError.Validacion, mensaje);
        Assert.Equal(0, await NuevoContexto().Comercios.CountAsync());
    }
}

public class ServicioSesionesTests : BaseDePrueba
{
    [Fact]
    public async Task Una_sesion_de_comercio_vigente_trae_los_datos_del_usuario()
    {
        var cafe = await ComercioAsync();
        var u = await UsuarioAsync(cafe, rol: RolUsuario.Cajero, nombre: "Juan (caja)");
        var (token, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);

        var s = await Sesiones.ValidarAsync(token);

        Assert.NotNull(s);
        Assert.Equal(u.Id, s!.UsuarioId);
        Assert.Equal(cafe.Id, s.ComercioId);
        Assert.Equal("Juan (caja)", s.Nombre);
        Assert.Equal(RolUsuario.Cajero, s.Rol);
    }

    [Fact]
    public async Task Una_sesion_vencida_no_sirve()
    {
        var u = await UsuarioAsync(await ComercioAsync());
        var (token, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);

        Reloj.Avanzar(TimeSpan.FromDays(7));

        Assert.Null(await Sesiones.ValidarAsync(token));
    }

    [Fact]
    public async Task Una_sesion_cerrada_no_sirve()
    {
        var cliente = await ClienteAsync();
        var (token, _) = await Sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: cliente.Id);
        var s = await Sesiones.ValidarAsync(token);

        await Sesiones.CerrarAsync(s!.SesionId);

        Assert.Null(await Sesiones.ValidarAsync(token));
    }

    [Fact]
    public async Task La_sesion_de_un_usuario_desactivado_no_sirve()
    {
        var u = await UsuarioAsync(await ComercioAsync(), rol: RolUsuario.Cajero);
        var (token, _) = await Sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);
        u.CambiarActivo(false);
        await Db.SaveChangesAsync();

        Assert.Null(await Sesiones.ValidarAsync(token));
    }

    [Theory]
    [InlineData("corto")]
    [InlineData("token-inventado-que-no-existe-en-la-base")]
    public async Task Un_token_invalido_no_sirve(string token)
    {
        Assert.Null(await Sesiones.ValidarAsync(token));
    }

    [Fact]
    public async Task La_sesion_de_registro_trae_el_telefono_verificado()
    {
        var (token, vence) = await Sesiones.AbrirAsync(TipoSesion.Registro, telefono: "1199990000");

        var s = await Sesiones.ValidarAsync(token);

        Assert.Equal("1199990000", s!.Telefono);
        Assert.Equal(Inicio.AddMinutes(30), vence);
    }

    [Fact]
    public async Task El_token_no_se_guarda_solo_su_hash()
    {
        var cliente = await ClienteAsync();
        var (token, _) = await Sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: cliente.Id);

        var guardada = await NuevoContexto().Sesiones.SingleAsync();
        Assert.NotEqual(token, guardada.TokenHash);
        Assert.Equal(Tokens.Hash(token), guardada.TokenHash);
    }
}

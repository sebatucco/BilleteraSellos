using BilleteraSellos.Aplicacion.Funcionalidades.Acceso;
using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

namespace BilleteraSellos.Aplicacion.Tests;

public class CodigoPorSmsTests : BaseDePrueba
{
    SolicitarCodigoHandler Solicitar() => new(Db, Sms, Reloj);
    VerificarCodigoHandler Verificar() => new(Db, Sesiones, Reloj);

    [Fact]
    public async Task Cliente_existente_entra_con_el_codigo()
    {
        var lucia = await ClienteAsync();

        var pedido = await Solicitar().Handle(new SolicitarCodigo("+54 9 11 5487-3320"), default);
        var r = await Verificar().Handle(new VerificarCodigo("011 15 5487-3320", Sms.UltimoCodigo), default);

        Assert.True(pedido.Exito);
        Assert.Equal(10, pedido.Valor.VenceEnMinutos);
        Assert.Equal(("1154873320", pedido.Valor.Codigo), Sms.Enviados.Single());
        Assert.True(r.Exito);
        Assert.False(r.Valor.RequiereRegistro);
        Assert.Equal(lucia.Id, r.Valor.ClienteId);
        Assert.Equal("Lucía Fernández", r.Valor.Nombre);
        var sesion = await Sesiones.ValidarAsync(r.Valor.Token);
        Assert.Equal(TipoSesion.Cliente, sesion!.Tipo);
        Assert.Equal(lucia.Id, sesion.ClienteId);
    }

    [Fact]
    public async Task Telefono_nuevo_recibe_un_token_de_registro()
    {
        await Solicitar().Handle(new SolicitarCodigo("1199990000"), default);

        var r = await Verificar().Handle(new VerificarCodigo("1199990000", Sms.UltimoCodigo), default);

        Assert.True(r.Exito);
        Assert.True(r.Valor.RequiereRegistro);
        Assert.Null(r.Valor.ClienteId);
        Assert.Equal(Inicio.AddMinutes(30), r.Valor.VenceEn);
        var sesion = await Sesiones.ValidarAsync(r.Valor.Token);
        Assert.Equal(TipoSesion.Registro, sesion!.Tipo);
        Assert.Equal("1199990000", sesion.Telefono);
    }

    [Fact]
    public async Task El_mismo_codigo_no_sirve_dos_veces()
    {
        await ClienteAsync();
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        var codigo = Sms.UltimoCodigo;
        Assert.True((await Verificar().Handle(new VerificarCodigo("1154873320", codigo), default)).Exito);

        var r = await Verificar().Handle(new VerificarCodigo("1154873320", codigo), default);

        AssertError(r, TipoError.NoAutorizado, "El código venció o no existe. Pedí uno nuevo.");
    }

    [Fact]
    public async Task Un_codigo_incorrecto_no_autoriza_y_al_quinto_intento_bloquea_el_codigo()
    {
        await ClienteAsync();
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        var correcto = Sms.UltimoCodigo;
        var incorrecto = correcto == "000000" ? "111111" : "000000";

        for (var i = 1; i <= 4; i++)
            AssertError(await Verificar().Handle(new VerificarCodigo("1154873320", incorrecto), default),
                TipoError.NoAutorizado, "El código no es correcto.");
        var quinto = await Verificar().Handle(new VerificarCodigo("1154873320", incorrecto), default);
        var conElCorrecto = await Verificar().Handle(new VerificarCodigo("1154873320", correcto), default);

        AssertError(quinto, TipoError.DemasiadosIntentos, "Demasiados intentos. Pedí un código nuevo.");
        AssertError(conElCorrecto, TipoError.DemasiadosIntentos, "Demasiados intentos. Pedí un código nuevo.");
    }

    [Fact]
    public async Task No_se_puede_pedir_otro_codigo_antes_de_un_minuto()
    {
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        Reloj.Avanzar(TimeSpan.FromSeconds(59));

        var r = await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);

        AssertError(r, TipoError.DemasiadosIntentos, "Ya te mandamos un código. Esperá un minuto para pedir otro.");
        Assert.Single(Sms.Enviados);
    }

    [Fact]
    public async Task Un_codigo_nuevo_anula_el_anterior()
    {
        await ClienteAsync();
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        var viejo = Sms.UltimoCodigo;
        Reloj.Avanzar(TimeSpan.FromMinutes(2));
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        var nuevo = Sms.UltimoCodigo;

        if (viejo != nuevo)
            AssertError(await Verificar().Handle(new VerificarCodigo("1154873320", viejo), default), TipoError.NoAutorizado);
        Assert.True((await Verificar().Handle(new VerificarCodigo("1154873320", nuevo), default)).Exito);
    }

    [Fact]
    public async Task Como_maximo_cinco_codigos_por_hora()
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await Solicitar().Handle(new SolicitarCodigo("1154873320"), default)).Exito);
            Reloj.Avanzar(TimeSpan.FromMinutes(2));
        }

        var sexto = await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);

        AssertError(sexto, TipoError.DemasiadosIntentos, "Pediste demasiados códigos. Probá de nuevo en una hora.");
    }

    [Fact]
    public async Task Si_el_sms_falla_devuelve_error_externo_y_el_codigo_anterior_sigue_sirviendo()
    {
        await ClienteAsync();
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        var anterior = Sms.UltimoCodigo;
        Reloj.Avanzar(TimeSpan.FromMinutes(2));
        Sms.Fallar = true;

        var r = await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);

        AssertError(r, TipoError.ServicioExterno, "No pudimos enviar el código. Probá de nuevo en unos minutos.");
        Assert.Equal(1, await NuevoContexto().CodigosVerificacion.CountAsync());
        Assert.True((await Verificar().Handle(new VerificarCodigo("1154873320", anterior), default)).Exito);
    }

    [Fact]
    public async Task El_codigo_vence_a_los_diez_minutos()
    {
        await ClienteAsync();
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);
        Reloj.Avanzar(TimeSpan.FromMinutes(10));

        var r = await Verificar().Handle(new VerificarCodigo("1154873320", Sms.UltimoCodigo), default);

        AssertError(r, TipoError.NoAutorizado, "El código venció o no existe. Pedí uno nuevo.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("12345678901234567")]
    public async Task Un_telefono_invalido_no_manda_nada(string telefono)
    {
        var r = await Solicitar().Handle(new SolicitarCodigo(telefono), default);

        AssertError(r, TipoError.Validacion, "Escribí el teléfono con código de área, por ejemplo 11 5487 3320.");
        Assert.Empty(Sms.Enviados);
    }

    [Fact]
    public async Task El_codigo_no_se_guarda_en_texto_plano()
    {
        await Solicitar().Handle(new SolicitarCodigo("1154873320"), default);

        var guardado = await NuevoContexto().CodigosVerificacion.SingleAsync();
        Assert.DoesNotContain(Sms.UltimoCodigo, guardado.CodigoHash);
    }
}

public class RegistrarClienteTests : BaseDePrueba
{
    RegistrarClienteHandler Handler() => new(Db, Sesiones, Reloj);

    [Fact]
    public async Task Crea_el_cliente_cierra_la_sesion_de_registro_y_abre_la_del_cliente()
    {
        var (tokenRegistro, _) = await Sesiones.AbrirAsync(TipoSesion.Registro, telefono: "1199990000");
        var registro = await Sesiones.ValidarAsync(tokenRegistro);

        var r = await Handler().Handle(new RegistrarCliente("1199990000", registro!.SesionId, " Ana Torres ", "35.123.456", new DateOnly(1990, 5, 2)), default);

        Assert.True(r.Exito);
        Assert.Equal("Ana Torres", r.Valor.Nombre);
        var cliente = await NuevoContexto().Clientes.SingleAsync();
        Assert.Equal("35123456", cliente.Dni);
        Assert.Equal("1199990000", cliente.Telefono);
        Assert.Equal(new DateOnly(1990, 5, 2), cliente.FechaNacimiento);
        Assert.Null(await Sesiones.ValidarAsync(tokenRegistro));
        var sesionCliente = await Sesiones.ValidarAsync(r.Valor.Token);
        Assert.Equal(cliente.Id, sesionCliente!.ClienteId);
    }

    [Fact]
    public async Task Dni_o_telefono_repetido_devuelve_conflicto()
    {
        await ClienteAsync(dni: "35123456", telefono: "1166667777");

        var mismoDni = await Handler().Handle(new RegistrarCliente("1199990000", null, "Ana", "35123456", null), default);
        var mismoTelefono = await Handler().Handle(new RegistrarCliente("1166667777", null, "Ana", "35999999", null), default);

        AssertError(mismoDni, TipoError.Conflicto, "Ya hay una cuenta con ese DNI o teléfono.");
        AssertError(mismoTelefono, TipoError.Conflicto, "Ya hay una cuenta con ese DNI o teléfono.");
    }

    [Fact]
    public async Task Datos_invalidos_no_crean_nada()
    {
        var r = await Handler().Handle(new RegistrarCliente("1199990000", null, "Ana", "123", null), default);

        AssertError(r, TipoError.Validacion, "El DNI tiene que tener entre 7 y 9 números.");
        Assert.Equal(0, await NuevoContexto().Clientes.CountAsync());
    }
}

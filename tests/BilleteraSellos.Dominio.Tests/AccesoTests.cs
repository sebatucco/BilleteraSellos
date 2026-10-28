namespace BilleteraSellos.Dominio.Tests;

public class UsuarioTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    static Usuario Cajero() => Usuario.Crear(1, " Juan (caja) ", "caja@cafe.demo", "hash", RolUsuario.Cajero);

    [Fact]
    public void Crear_recorta_el_nombre_y_queda_activo_sin_bloqueo()
    {
        var u = Cajero();
        Assert.Equal("Juan (caja)", u.Nombre);
        Assert.True(u.Activo);
        Assert.False(u.EstaBloqueado(Ahora));
        Assert.Equal(RolUsuario.Cajero, u.Rol);
    }

    [Fact]
    public void Cuatro_fallos_no_bloquean()
    {
        var u = Cajero();
        for (var i = 0; i < 4; i++) u.RegistrarFalla(Ahora);

        Assert.False(u.EstaBloqueado(Ahora));
        Assert.Equal(4, u.IntentosFallidos);
    }

    [Fact]
    public void Cinco_fallos_bloquean_quince_minutos()
    {
        var u = Cajero();
        for (var i = 0; i < 5; i++) u.RegistrarFalla(Ahora);

        Assert.True(u.EstaBloqueado(Ahora));
        Assert.True(u.EstaBloqueado(Ahora.AddMinutes(14)));
        Assert.Equal(Ahora.AddMinutes(15), u.BloqueadoHasta);
        Assert.Equal(0, u.IntentosFallidos); // el contador vuelve a cero al bloquear
    }

    [Fact]
    public void El_bloqueo_vence_a_los_quince_minutos()
    {
        var u = Cajero();
        for (var i = 0; i < 5; i++) u.RegistrarFalla(Ahora);

        Assert.False(u.EstaBloqueado(Ahora.AddMinutes(15)));
        Assert.False(u.EstaBloqueado(Ahora.AddMinutes(16)));
    }

    [Fact]
    public void Un_ingreso_exitoso_reinicia_los_intentos()
    {
        var u = Cajero();
        for (var i = 0; i < 3; i++) u.RegistrarFalla(Ahora);

        u.RegistrarIngreso();
        for (var i = 0; i < 4; i++) u.RegistrarFalla(Ahora);

        Assert.False(u.EstaBloqueado(Ahora));
        Assert.Equal(4, u.IntentosFallidos);
    }

    [Fact]
    public void Restablecer_cambia_la_contrasena_y_desbloquea()
    {
        var u = Cajero();
        for (var i = 0; i < 5; i++) u.RegistrarFalla(Ahora);

        u.Restablecer("hash-nuevo");

        Assert.Equal("hash-nuevo", u.PasswordHash);
        Assert.False(u.EstaBloqueado(Ahora));
        Assert.Null(u.BloqueadoHasta);
        Assert.Equal(0, u.IntentosFallidos);
    }

    [Fact]
    public void CambiarContrasena_no_desbloquea()
    {
        var u = Cajero();
        for (var i = 0; i < 5; i++) u.RegistrarFalla(Ahora);

        u.CambiarContrasena("hash-nuevo");

        Assert.Equal("hash-nuevo", u.PasswordHash);
        Assert.True(u.EstaBloqueado(Ahora));
    }

    [Fact]
    public void CambiarActivo_desactiva_y_reactiva()
    {
        var u = Cajero();
        u.CambiarActivo(false);
        Assert.False(u.Activo);
        u.CambiarActivo(true);
        Assert.True(u.Activo);
    }
}

public class SesionTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TipoSesion.Comercio, 7 * 24 * 60)]
    [InlineData(TipoSesion.Cliente, 90 * 24 * 60)]
    [InlineData(TipoSesion.Registro, 30)]
    public void Cada_tipo_de_sesion_dura_lo_suyo(TipoSesion tipo, int minutos)
    {
        var s = Sesion.Abrir(tipo, "hash", Ahora);

        Assert.Equal(Ahora.AddMinutes(minutos), s.VenceEn);
        Assert.True(s.EstaVigente(Ahora.AddMinutes(minutos).AddSeconds(-1)));
        Assert.False(s.EstaVigente(Ahora.AddMinutes(minutos)));
    }

    [Fact]
    public void Una_sesion_revocada_deja_de_estar_vigente()
    {
        var s = Sesion.Abrir(TipoSesion.Comercio, "hash", Ahora, usuarioId: 3);

        s.Revocar(Ahora.AddMinutes(1));

        Assert.False(s.EstaVigente(Ahora.AddMinutes(2)));
        Assert.Equal(Ahora.AddMinutes(1), s.RevocadaEn);
    }

    [Fact]
    public void Revocar_dos_veces_conserva_la_primera_fecha()
    {
        var s = Sesion.Abrir(TipoSesion.Cliente, "hash", Ahora, clienteId: 1);
        s.Revocar(Ahora.AddMinutes(1));
        s.Revocar(Ahora.AddMinutes(9));
        Assert.Equal(Ahora.AddMinutes(1), s.RevocadaEn);
    }

    [Fact]
    public void Abrir_guarda_los_datos_de_quien_entra()
    {
        var s = Sesion.Abrir(TipoSesion.Registro, "hash", Ahora, telefono: "1199990000");
        Assert.Equal("1199990000", s.Telefono);
        Assert.Null(s.UsuarioId);
        Assert.Null(s.ClienteId);
        Assert.Equal("hash", s.TokenHash);
    }
}

public class CodigoVerificacionTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Vence_a_los_diez_minutos()
    {
        var c = CodigoVerificacion.Crear("1154873320", "hash", Ahora);
        Assert.Equal(Ahora.AddMinutes(10), c.VenceEn);
    }

    [Fact]
    public void Agota_los_intentos_al_quinto()
    {
        var c = CodigoVerificacion.Crear("1154873320", "hash", Ahora);
        for (var i = 0; i < 4; i++) c.RegistrarIntento();
        Assert.False(c.AgotoIntentos);

        c.RegistrarIntento();
        Assert.True(c.AgotoIntentos);
    }

    [Fact]
    public void Anular_no_pisa_un_uso_anterior()
    {
        var c = CodigoVerificacion.Crear("1154873320", "hash", Ahora);
        c.Usar(Ahora.AddMinutes(1));
        c.Anular(Ahora.AddMinutes(5));
        Assert.Equal(Ahora.AddMinutes(1), c.UsadoEn);
    }

    [Fact]
    public void Anular_marca_como_usado()
    {
        var c = CodigoVerificacion.Crear("1154873320", "hash", Ahora);
        c.Anular(Ahora.AddMinutes(2));
        Assert.Equal(Ahora.AddMinutes(2), c.UsadoEn);
    }
}

public class TokenRestablecimientoTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Vale_una_hora()
    {
        var t = TokenRestablecimiento.Crear(1, "hash", Ahora);
        Assert.True(t.EstaVigente(Ahora.AddMinutes(59)));
        Assert.False(t.EstaVigente(Ahora.AddHours(1)));
    }

    [Fact]
    public void Usado_deja_de_estar_vigente()
    {
        var t = TokenRestablecimiento.Crear(1, "hash", Ahora);
        t.Usar(Ahora.AddMinutes(5));
        Assert.False(t.EstaVigente(Ahora.AddMinutes(6)));
    }

    [Fact]
    public void Usar_dos_veces_conserva_la_primera_fecha()
    {
        var t = TokenRestablecimiento.Crear(1, "hash", Ahora);
        t.Usar(Ahora.AddMinutes(5));
        t.Usar(Ahora.AddMinutes(30));
        Assert.Equal(Ahora.AddMinutes(5), t.UsadoEn);
    }
}

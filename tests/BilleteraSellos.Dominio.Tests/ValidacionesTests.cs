namespace BilleteraSellos.Dominio.Tests;

public class ClienteTests
{
    static readonly DateOnly Hoy = new(2026, 10, 15);

    [Fact]
    public void Registrar_con_datos_validos_limpia_nombre_y_dni()
    {
        var r = Cliente.Registrar("  Lucía Fernández ", "30.111.222", "1154873320", new DateOnly(1994, 3, 8), Hoy);

        Assert.True(r.Exito);
        Assert.Equal("Lucía Fernández", r.Valor.Nombre);
        Assert.Equal("30111222", r.Valor.Dni);
        Assert.Equal("1154873320", r.Valor.Telefono);
        Assert.Equal(new DateOnly(1994, 3, 8), r.Valor.FechaNacimiento);
        Assert.Equal(32, r.Valor.CodigoQr.Length); // un GUID, no el DNI
    }

    [Fact]
    public void Cada_cliente_tiene_su_propio_codigo_qr()
    {
        var a = Cliente.Registrar("Ana", "35123456", "1166667777", null, Hoy).Valor;
        var b = Cliente.Registrar("Bea", "35123457", "1166667778", null, Hoy).Valor;
        Assert.NotEqual(a.CodigoQr, b.CodigoQr);
    }

    [Theory]
    [InlineData("", "30111222", "1154873320", "Falta el nombre.")]
    [InlineData("   ", "30111222", "1154873320", "Falta el nombre.")]
    [InlineData(null, "30111222", "1154873320", "Falta el nombre.")]
    [InlineData("Ana", "123456", "1154873320", "El DNI tiene que tener entre 7 y 9 números.")]
    [InlineData("Ana", "1234567890", "1154873320", "El DNI tiene que tener entre 7 y 9 números.")]
    [InlineData("Ana", null, "1154873320", "El DNI tiene que tener entre 7 y 9 números.")]
    [InlineData("Ana", "30111222", "1234567", "El teléfono tiene que tener entre 8 y 13 números, con código de área.")]
    [InlineData("Ana", "30111222", "12345678901234", "El teléfono tiene que tener entre 8 y 13 números, con código de área.")]
    public void Registrar_con_datos_invalidos_devuelve_el_motivo(string? nombre, string? dni, string telefono, string mensaje)
    {
        var r = Cliente.Registrar(nombre, dni, telefono, null, Hoy);

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal(mensaje, r.Error.Mensaje);
    }

    [Theory]
    [InlineData(2026, 10, 16)] // mañana
    [InlineData(1899, 12, 31)]
    public void Registrar_con_fecha_de_nacimiento_imposible_falla(int anio, int mes, int dia)
    {
        var r = Cliente.Registrar("Ana", "30111222", "1154873320", new DateOnly(anio, mes, dia), Hoy);

        Assert.False(r.Exito);
        Assert.Equal("La fecha de nacimiento no es válida.", r.Error!.Mensaje);
    }

    [Fact]
    public void Registrar_acepta_nacido_hoy_y_sin_fecha()
    {
        Assert.True(Cliente.Registrar("Ana", "30111222", "1154873320", Hoy, Hoy).Exito);
        Assert.True(Cliente.Registrar("Ana", "30111222", "1154873320", null, Hoy).Exito);
    }
}

public class ProgramaSellosTests
{
    [Fact]
    public void Nuevo_tiene_valores_por_defecto_razonables()
    {
        var p = ProgramaSellos.Nuevo(10, "Café gratis", 900);
        Assert.Equal(10, p.SellosNecesarios);
        Assert.Equal(7, p.DiasParaRetirar);
        Assert.Equal(1, p.HorasEntreSellos);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(31)]
    public void Actualizar_fuera_de_3_a_30_sellos_falla(int sellos)
    {
        var p = ProgramaSellos.Nuevo(10, "Café gratis", 900);

        var r = p.Actualizar(sellos, "Café gratis", 900, 7, 1);

        Assert.False(r.Exito);
        Assert.Equal(TipoError.Validacion, r.Error!.Tipo);
        Assert.Equal("La tarjeta tiene que pedir entre 3 y 30 sellos.", r.Error.Mensaje);
        Assert.Equal(10, p.SellosNecesarios);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData((string?)null)]
    public void Actualizar_sin_premio_falla(string? premio)
    {
        var p = ProgramaSellos.Nuevo(10, "Café gratis", 900);

        var r = p.Actualizar(8, premio, 900, 7, 1);

        Assert.False(r.Exito);
        Assert.Equal("Escribí cuál es el premio.", r.Error!.Mensaje);
    }

    [Fact]
    public void Actualizar_guarda_y_acota_dias_y_horas()
    {
        var p = ProgramaSellos.Nuevo(10, "Café gratis", 900);

        Assert.True(p.Actualizar(3, "  Medialuna  ", 500, 999, -5).Exito);
        Assert.Equal(3, p.SellosNecesarios);
        Assert.Equal("Medialuna", p.Premio);
        Assert.Equal(500m, p.CostoPremio);
        Assert.Equal(365, p.DiasParaRetirar);
        Assert.Equal(0, p.HorasEntreSellos);

        Assert.True(p.Actualizar(30, "Medialuna", 500, 0, 100).Exito);
        Assert.Equal(1, p.DiasParaRetirar);
        Assert.Equal(72, p.HorasEntreSellos);
    }
}

public class ResultadoTests
{
    [Fact]
    public void Leer_el_valor_de_un_resultado_fallido_lanza_excepcion()
    {
        Resultado<int> r = Error.Conflicto("no");
        Assert.False(r.Exito);
        Assert.Throws<InvalidOperationException>(() => r.Valor);
    }

    [Fact]
    public void Un_valor_se_convierte_en_resultado_exitoso()
    {
        Resultado<string> r = "hola";
        Assert.True(r.Exito);
        Assert.Null(r.Error);
        Assert.Equal("hola", r.Valor);
    }
}

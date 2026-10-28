namespace BilleteraSellos.Dominio.Tests;

public class NivelesTests
{
    [Theory]
    [InlineData(0, Nivel.Bronce, 10)]
    [InlineData(9, Nivel.Bronce, 1)]
    [InlineData(10, Nivel.Plata, 20)]
    [InlineData(29, Nivel.Plata, 1)]
    [InlineData(30, Nivel.Oro, null)]
    [InlineData(120, Nivel.Oro, null)]
    public void Calcula_el_nivel_y_lo_que_falta(int sellos, Nivel nivel, int? faltan)
    {
        Assert.Equal(nivel, Niveles.Calcular(sellos));
        Assert.Equal(faltan, Niveles.FaltanParaSiguiente(sellos));
    }
}

public class EnfriamientoTests
{
    static readonly DateTime Ahora = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Visitas cada <paramref name="cada"/> días, la última hace <paramref name="ultimaHace"/> días.</summary>
    static List<DateTime> Visitas(int cantidad, int cada, int ultimaHace) =>
        Enumerable.Range(0, cantidad).Select(i => Ahora.AddDays(-ultimaHace - i * cada)).ToList();

    [Fact]
    public void Sin_visitas_no_tiene_historial()
    {
        var r = Enfriamiento.Evaluar([], Ahora);
        Assert.Equal(EstadoCliente.SinHistorial, r.Estado);
        Assert.False(Enfriamiento.EstaEnfriandose(r));
    }

    [Fact]
    public void Con_una_sola_visita_no_hay_ritmo_pero_si_dias_sin_venir()
    {
        var r = Enfriamiento.Evaluar([Ahora.AddDays(-40)], Ahora);
        Assert.Equal(EstadoCliente.SinHistorial, r.Estado);
        Assert.Equal(40, r.DiasSinVenir);
        Assert.Null(r.CadaCuantosDias);
    }

    [Fact]
    public void Dentro_de_su_ritmo_esta_al_dia()
    {
        var r = Enfriamiento.Evaluar(Visitas(5, cada: 5, ultimaHace: 6), Ahora);
        Assert.Equal(EstadoCliente.AlDia, r.Estado);
        Assert.Equal((double?)5, r.CadaCuantosDias);
    }

    [Fact]
    public void El_doble_de_su_ritmo_es_riesgo()
    {
        var r = Enfriamiento.Evaluar(Visitas(5, cada: 5, ultimaHace: 10), Ahora);
        Assert.Equal(EstadoCliente.EnRiesgo, r.Estado);
        Assert.Equal((double?)2, r.Relacion);
        Assert.True(Enfriamiento.EstaEnfriandose(r));
    }

    [Fact]
    public void El_triple_de_su_ritmo_es_critico()
    {
        var r = Enfriamiento.Evaluar(Visitas(9, cada: 5, ultimaHace: 24), Ahora);
        Assert.Equal(EstadoCliente.Critico, r.Estado);
        Assert.Equal(24, r.DiasSinVenir);
    }

    [Fact]
    public void Se_mide_contra_su_propio_ritmo_y_no_contra_dias_fijos()
    {
        // 30 días sin venir es normal para quien viene cada mes, pero crítico para quien viene cada 3 días.
        var mensual = Enfriamiento.Evaluar(Visitas(4, cada: 30, ultimaHace: 30), Ahora);
        var frecuente = Enfriamiento.Evaluar(Visitas(10, cada: 3, ultimaHace: 30), Ahora);

        Assert.Equal(EstadoCliente.AlDia, mensual.Estado);
        Assert.Equal(EstadoCliente.Critico, frecuente.Estado);
    }

    [Fact]
    public void Dos_visitas_el_mismo_dia_cuentan_como_ritmo_de_un_dia()
    {
        var r = Enfriamiento.Evaluar([Ahora.AddDays(-3).AddHours(-1), Ahora.AddDays(-3)], Ahora);
        Assert.Equal((double?)1, r.CadaCuantosDias);
        Assert.Equal(EstadoCliente.Critico, r.Estado);
    }

    [Fact]
    public void El_orden_de_las_visitas_no_importa()
    {
        var visitas = Visitas(5, cada: 5, ultimaHace: 10);
        var desordenadas = visitas.OrderBy(v => v.Ticks % 7).ToList();
        Assert.Equal(Enfriamiento.Evaluar(visitas, Ahora), Enfriamiento.Evaluar(desordenadas, Ahora));
    }
}

public class CalculadoraCostoTests
{
    [Fact]
    public void Caso_del_cafe_10_sellos_premio_900_ticket_4500_600_compras()
    {
        var c = CalculadoraCosto.Calcular(10, 900, 4500, 600);

        Assert.Equal(60.0, c.PremiosPorMes);
        Assert.Equal(54000m, c.CostoMensual);
        Assert.Equal(2_700_000m, c.VentasMensuales);
        Assert.Equal(2.0, c.PorcentajeDeVentas);
        Assert.Equal(90m, c.CostoPorCompra);
        Assert.Equal("Sano", c.Semaforo);
    }

    [Theory]
    [InlineData(300, "Sano")]      // 3,0% justo
    [InlineData(301, "Revisalo")]  // 3,01%
    [InlineData(600, "Revisalo")]  // 6,0% justo
    [InlineData(601, "Caro")]      // 6,01%
    public void Semaforo_en_los_bordes_de_3_y_6_por_ciento(int costoPremio, string semaforo)
    {
        // 10 sellos, ticket 1000, 100 compras: ventas 100.000; costo = costoPremio * 10.
        var c = CalculadoraCosto.Calcular(10, costoPremio, 1000, 100);
        Assert.Equal(semaforo, c.Semaforo);
    }

    [Fact]
    public void Sin_ventas_el_porcentaje_es_cero()
    {
        var c = CalculadoraCosto.Calcular(10, 900, 0, 0);
        Assert.Equal(0.0, c.PorcentajeDeVentas);
        Assert.Equal("Sano", c.Semaforo);
    }

    [Fact]
    public void Cero_sellos_no_tiene_sentido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraCosto.Calcular(0, 900, 4500, 600));
    }
}

public class TelefonosTests
{
    [Theory]
    [InlineData("+54 9 11 5487-3320", "1154873320")]
    [InlineData("5491154873320", "1154873320")]
    [InlineData("54 11 5487 3320", "1154873320")]
    [InlineData("011 15 5487-3320", "1154873320")]
    [InlineData("11 15 5487 3320", "1154873320")]
    [InlineData("11 5487 3320", "1154873320")]
    [InlineData("1154873320", "1154873320")]
    [InlineData("0351 15 612-3456", "3516123456")]   // Córdoba: característica de 3 dígitos
    [InlineData("02954 15 41-2345", "2954412345")]   // Santa Rosa: característica de 4 dígitos
    [InlineData("+54 9 351 612-3456", "3516123456")]
    public void Normaliza_a_caracteristica_mas_numero(string entrada, string esperado)
    {
        Assert.Equal(esperado, Telefonos.Normalizar(entrada));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("sin numeros", "")]
    public void Sin_digitos_queda_vacio(string? entrada, string esperado)
    {
        Assert.Equal(esperado, Telefonos.Normalizar(entrada));
    }

    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData("1234567890123", true)]
    [InlineData("12345678901234", false)]
    public void Es_valido_entre_8_y_13_digitos(string normalizado, bool valido)
    {
        Assert.Equal(valido, Telefonos.EsValido(normalizado));
    }
}

public class EmailsTests
{
    [Theory]
    [InlineData("CAFE@demo.com ", "cafe@demo.com")]
    [InlineData("  Juan.Perez@Ejemplo.COM.ar", "juan.perez@ejemplo.com.ar")]
    public void Normaliza_a_minusculas_y_sin_espacios(string entrada, string esperado)
    {
        Assert.Equal(esperado, Emails.Normalizar(entrada));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba")]
    [InlineData("dos@@arrobas.com")]
    public void Un_email_invalido_devuelve_null(string? entrada)
    {
        Assert.Null(Emails.Normalizar(entrada));
    }

    [Fact]
    public void Un_email_de_mas_de_200_caracteres_es_invalido()
    {
        Assert.Null(Emails.Normalizar(new string('a', 195) + "@x.com"));
    }
}

public class PoliticaContrasenasTests
{
    [Theory]
    [InlineData("Demo1234")]
    [InlineData("abcdefg1")]
    [InlineData("contraseña larga 2026")]
    public void Acepta_8_o_mas_con_letras_y_numeros(string contrasena)
    {
        Assert.Null(PoliticaContrasenas.Validar(contrasena));
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("Abc123")]
    [InlineData("Abcd123")]
    public void Rechaza_las_cortas(string? contrasena)
    {
        Assert.Equal("La contraseña tiene que tener al menos 8 caracteres.", PoliticaContrasenas.Validar(contrasena));
    }

    [Theory]
    [InlineData("solamenteletras")]
    [InlineData("12345678")]
    public void Rechaza_sin_letras_o_sin_numeros(string contrasena)
    {
        Assert.Equal("La contraseña tiene que tener letras y números.", PoliticaContrasenas.Validar(contrasena));
    }
}

public class CumpleanosTests
{
    [Fact]
    public void Es_su_cumpleanos_el_mismo_dia_y_mes()
    {
        Assert.True(Cumpleanos.EsSuCumpleanos(new DateOnly(1994, 3, 8), new DateOnly(2026, 3, 8)));
        Assert.False(Cumpleanos.EsSuCumpleanos(new DateOnly(1994, 3, 8), new DateOnly(2026, 3, 9)));
        Assert.False(Cumpleanos.EsSuCumpleanos(null, new DateOnly(2026, 3, 8)));
    }

    [Fact]
    public void Nacidos_el_29_de_febrero_festejan_el_28_en_anios_no_bisiestos()
    {
        var nacimiento = new DateOnly(2000, 2, 29);
        Assert.True(Cumpleanos.EsSuCumpleanos(nacimiento, new DateOnly(2026, 2, 28)));
        Assert.False(Cumpleanos.EsSuCumpleanos(nacimiento, new DateOnly(2026, 3, 1)));
        Assert.True(Cumpleanos.EsSuCumpleanos(nacimiento, new DateOnly(2028, 2, 29)));
        Assert.False(Cumpleanos.EsSuCumpleanos(nacimiento, new DateOnly(2028, 2, 28)));
    }

    [Fact]
    public void Proximo_es_hoy_si_cumple_hoy()
    {
        Assert.Equal(new DateOnly(2026, 10, 15), Cumpleanos.Proximo(new DateOnly(1990, 10, 15), new DateOnly(2026, 10, 15)));
    }

    [Fact]
    public void Proximo_cruza_al_anio_siguiente_si_ya_paso()
    {
        Assert.Equal(new DateOnly(2027, 1, 3), Cumpleanos.Proximo(new DateOnly(1990, 1, 3), new DateOnly(2026, 12, 28)));
    }

    [Fact]
    public void Proximo_del_29_de_febrero_en_anio_no_bisiesto_es_el_28()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), Cumpleanos.Proximo(new DateOnly(2000, 2, 29), new DateOnly(2026, 10, 15)));
        Assert.Equal(new DateOnly(2028, 2, 29), Cumpleanos.Proximo(new DateOnly(2000, 2, 29), new DateOnly(2027, 3, 1)));
    }
}

public class CodigosComercioTests
{
    [Theory]
    [InlineData("Café Tostado", "cafe-tostado")]
    [InlineData("Panadería La Espiga", "panaderia-la-espiga")]
    [InlineData("Verdulería Doña Rosa", "verduleria-dona-rosa")]
    [InlineData("  Pizza & Birra!!  ", "pizza-birra")]
    [InlineData("Kiosco 24 hs.", "kiosco-24-hs")]
    [InlineData("!!!", "comercio")]
    [InlineData("", "comercio")]
    public void Arma_el_codigo_del_qr_a_partir_del_nombre(string nombre, string esperado)
    {
        Assert.Equal(esperado, CodigosComercio.Base(nombre));
    }

    [Fact]
    public void El_codigo_tiene_como_maximo_30_caracteres_y_no_termina_en_guion()
    {
        var codigo = CodigosComercio.Base("Distribuidora Mayorista de Bebidas y Alimentos del Norte");
        Assert.True(codigo.Length <= 30);
        Assert.False(codigo.EndsWith('-'));
        Assert.StartsWith("distribuidora-mayorista", codigo);
    }
}

public class CodigosTests
{
    [Fact]
    public void Genera_codigos_de_6_caracteres_sin_letras_confusas()
    {
        for (var i = 0; i < 500; i++)
        {
            var codigo = Codigos.Generar();
            Assert.Equal(6, codigo.Length);
            Assert.DoesNotContain(codigo, ch => "01OIL".Contains(ch));
            Assert.All(codigo, ch => Assert.True(char.IsDigit(ch) || char.IsUpper(ch)));
        }
    }

    [Fact]
    public void Respeta_el_largo_pedido()
    {
        Assert.Equal(10, Codigos.Generar(10).Length);
    }

    [Fact]
    public void Los_codigos_varian()
    {
        var codigos = Enumerable.Range(0, 200).Select(_ => Codigos.Generar()).ToHashSet();
        Assert.True(codigos.Count > 190);
    }

    [Theory]
    [InlineData("30.111.222", "30111222")]
    [InlineData("+54 11 5487-3320", "541154873320")]
    [InlineData(null, "")]
    public void SoloDigitos_saca_todo_lo_demas(string? entrada, string esperado)
    {
        Assert.Equal(esperado, Codigos.SoloDigitos(entrada));
    }
}

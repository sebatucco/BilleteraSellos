using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;
using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

namespace BilleteraSellos.Aplicacion.Tests;

/// <summary>QR de la compra: la caja lo genera con el monto y el cliente lo escanea con su billetera.</summary>
public class QrCompraTests : BaseDePrueba
{
    GenerarQrCompraHandler Generar() => new(Db, Reloj);
    UsarQrCompraHandler Usar() => new(Db, Notificador, Reloj);
    ConsultarQrCompraHandler Consultar() => new(Db, Reloj);
    AnularQrCompraHandler Anular() => new(Db, Reloj);

    async Task<(Comercio Super, Usuario Cajera, Cliente Lucia)> SuperAsync(int horasEntreSellos = 0, int sellos = 10)
    {
        var super = await ComercioAsync("Supermercado Gómez", "supermercado-gomez", sellos: sellos, horasEntreSellos: horasEntreSellos,
            premio: "$5.000 de descuento", montoPorSello: 10000, compraMinima: 5000);
        var cajera = await UsuarioAsync(super, "caja1@super.demo", rol: RolUsuario.Cajero, nombre: "Ana (caja 1)");
        var lucia = await ClienteAsync();
        return (super, cajera, lucia);
    }

    [Fact]
    public async Task Generar_calcula_los_sellos_con_el_monto_y_guarda_solo_el_hash()
    {
        var (super, cajera, _) = await SuperAsync();

        var r = await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 35000), default);

        Assert.True(r.Exito);
        Assert.Equal(3, r.Valor.Sellos);
        Assert.Equal(35000, r.Valor.Monto);
        Assert.Equal(Inicio.AddMinutes(3), r.Valor.VenceEn);
        var guardada = await NuevoContexto().ComprasQr.SingleAsync();
        Assert.Equal(Tokens.Hash(r.Valor.Codigo), guardada.CodigoHash);
        Assert.NotEqual(r.Valor.Codigo, guardada.CodigoHash);
        Assert.Equal(cajera.Id, guardada.UsuarioId);
    }

    [Fact]
    public async Task Generar_valida_el_monto_con_las_reglas_del_programa()
    {
        var (super, cajera, _) = await SuperAsync();

        AssertError(await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, null), default), TipoError.Validacion, "Escribí el monto de la compra.");
        AssertError(await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 3000), default), TipoError.Validacion,
            "La compra mínima para sumar sellos es de $5.000.");
        Assert.Empty(await NuevoContexto().ComprasQr.ToListAsync());
    }

    [Fact]
    public async Task Usar_suma_los_sellos_crea_la_tarjeta_y_marca_el_QR()
    {
        var (super, cajera, lucia) = await SuperAsync();
        var qr = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 35000), default)).Valor;
        Reloj.Avanzar(TimeSpan.FromMinutes(1));

        var r = await Usar().Handle(new UsarQrCompra(lucia.Id, qr.Codigo), default);

        Assert.True(r.Exito);
        Assert.Equal("Supermercado Gómez", r.Valor.Comercio);
        Assert.Equal(super.Id, r.Valor.ComercioId);
        Assert.Equal(3, r.Valor.SellosSumados);
        Assert.Equal(3, r.Valor.SellosActuales);
        Assert.Equal(10, r.Valor.SellosNecesarios);
        Assert.True(r.Valor.TarjetaNueva);
        Assert.False(r.Valor.TarjetaCompleta);

        var ctx = NuevoContexto();
        var compra = await ctx.ComprasQr.SingleAsync();
        Assert.Equal(lucia.Id, compra.ClienteId);
        Assert.Equal(Inicio.AddMinutes(1), compra.UsadaEn);
        var mov = (await ctx.Tarjetas.Include(t => t.Movimientos).SingleAsync()).Movimientos.Single();
        Assert.Equal(3, mov.Cantidad);
        Assert.Equal(35000, mov.Monto);
        Assert.Equal("Ana (caja 1)", mov.CargadoPor);
    }

    [Fact]
    public async Task El_mismo_QR_no_sirve_dos_veces()
    {
        var (super, cajera, lucia) = await SuperAsync();
        var ana = await ClienteAsync("Ana Torres", "35123456", "1166667777");
        var qr = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 20000), default)).Valor;
        Assert.True((await Usar().Handle(new UsarQrCompra(lucia.Id, qr.Codigo), default)).Exito);

        AssertError(await Usar().Handle(new UsarQrCompra(ana.Id, qr.Codigo), default), TipoError.Conflicto, "Ese QR ya se usó.");
        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, qr.Codigo), default), TipoError.Conflicto, "Ese QR ya se usó.");
        Assert.Equal(2, (await NuevoContexto().Tarjetas.SingleAsync()).SellosActuales);
    }

    [Fact]
    public async Task Vencido_no_suma()
    {
        var (super, cajera, lucia) = await SuperAsync();
        var qr = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 20000), default)).Valor;
        Reloj.Avanzar(CompraQr.Vigencia);

        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, qr.Codigo), default), TipoError.Conflicto,
            "Ese QR venció. Pedile al cajero uno nuevo.");
        Assert.Empty(await NuevoContexto().Tarjetas.ToListAsync());
    }

    [Fact]
    public async Task Un_codigo_que_no_existe_da_no_encontrado()
    {
        var (_, _, lucia) = await SuperAsync();

        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, Tokens.Generar()), default), TipoError.NoEncontrado, "Ese QR no existe.");
        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, "corto"), default), TipoError.NoEncontrado, "Ese QR no existe.");
        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, null), default), TipoError.NoEncontrado, "Ese QR no existe.");
    }

    [Fact]
    public async Task Si_la_espera_entre_sellos_no_deja_el_QR_no_se_consume()
    {
        var (super, cajera, lucia) = await SuperAsync(horasEntreSellos: 1);
        var primero = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 20000), default)).Valor;
        Assert.True((await Usar().Handle(new UsarQrCompra(lucia.Id, primero.Codigo), default)).Exito);
        Reloj.Avanzar(TimeSpan.FromMinutes(10));
        var segundo = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 20000), default)).Valor;

        var r = await Usar().Handle(new UsarQrCompra(lucia.Id, segundo.Codigo), default);

        AssertError(r, TipoError.Conflicto);
        var estado = await Consultar().Handle(new ConsultarQrCompra(super.Id, segundo.Codigo), default);
        Assert.Equal(EstadoCompraQr.Pendiente, estado.Valor.Estado);
        Assert.Equal(2, (await NuevoContexto().Tarjetas.SingleAsync()).SellosActuales);
    }

    [Fact]
    public async Task Avisa_tarjeta_completa_solo_cuando_la_compra_la_completa()
    {
        var (super, cajera, lucia) = await SuperAsync(sellos: 5);
        var a = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 40000), default)).Valor;
        var b = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 30000), default)).Valor;
        var c = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 10000), default)).Valor;

        var r1 = await Usar().Handle(new UsarQrCompra(lucia.Id, a.Codigo), default);
        Assert.Empty(Notificador.Enviados);
        var r2 = await Usar().Handle(new UsarQrCompra(lucia.Id, b.Codigo), default);
        var r3 = await Usar().Handle(new UsarQrCompra(lucia.Id, c.Codigo), default);

        Assert.False(r1.Valor.TarjetaCompleta);
        Assert.True(r2.Valor.TarjetaCompleta);
        Assert.Equal(7, r2.Valor.SellosActuales);
        Assert.Equal(8, r3.Valor.SellosActuales);
        var aviso = Assert.Single(Notificador.Enviados);
        Assert.Equal(TipoAviso.TarjetaCompleta, aviso.Tipo);
    }

    [Fact]
    public async Task La_caja_consulta_el_estado_y_ve_quien_lo_uso()
    {
        var (super, cajera, lucia) = await SuperAsync();
        var qr = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 35000), default)).Valor;

        var antes = await Consultar().Handle(new ConsultarQrCompra(super.Id, qr.Codigo), default);
        await Usar().Handle(new UsarQrCompra(lucia.Id, qr.Codigo), default);
        var despues = await Consultar().Handle(new ConsultarQrCompra(super.Id, qr.Codigo), default);

        Assert.Equal(EstadoCompraQr.Pendiente, antes.Valor.Estado);
        Assert.Null(antes.Valor.Cliente);
        Assert.Equal(EstadoCompraQr.Usada, despues.Valor.Estado);
        Assert.Equal("Lucía F.", despues.Valor.Cliente);
        Assert.Equal(3, despues.Valor.Sellos);
    }

    [Fact]
    public async Task Otro_comercio_no_ve_ni_anula_el_QR()
    {
        var (super, cajera, _) = await SuperAsync();
        var cafe = await ComercioAsync();
        var qr = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 35000), default)).Valor;

        AssertError(await Consultar().Handle(new ConsultarQrCompra(cafe.Id, qr.Codigo), default), TipoError.NoEncontrado, "Ese QR no existe.");
        AssertError(await Anular().Handle(new AnularQrCompra(cafe.Id, qr.Codigo), default), TipoError.NoEncontrado, "Ese QR no existe.");
        Assert.Null((await NuevoContexto().ComprasQr.SingleAsync()).AnuladaEn);
    }

    [Fact]
    public async Task Anular_un_QR_pendiente_y_no_uno_usado()
    {
        var (super, cajera, lucia) = await SuperAsync();
        var pendiente = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 12000), default)).Valor;
        var usado = (await Generar().Handle(new GenerarQrCompra(super.Id, cajera.Id, 12000), default)).Valor;
        await Usar().Handle(new UsarQrCompra(lucia.Id, usado.Codigo), default);

        Assert.True((await Anular().Handle(new AnularQrCompra(super.Id, pendiente.Codigo), default)).Exito);
        AssertError(await Anular().Handle(new AnularQrCompra(super.Id, usado.Codigo), default), TipoError.Conflicto,
            "Ese QR ya se usó: no se puede anular.");
        AssertError(await Usar().Handle(new UsarQrCompra(lucia.Id, pendiente.Codigo), default), TipoError.Conflicto,
            "Ese QR fue anulado. Pedile al cajero uno nuevo.");
    }
}

public class SumarSelloConMontoTests : BaseDePrueba
{
    SumarSelloHandler Handler() => new(Db, Notificador, Reloj);

    [Fact]
    public async Task Con_monto_suma_los_sellos_que_corresponden()
    {
        var super = await ComercioAsync("Supermercado Gómez", "supermercado-gomez", montoPorSello: 10000);
        var lucia = await ClienteAsync();

        var r = await Handler().Handle(new SumarSello(super.Id, lucia.Id, "caja", 25000), default);

        Assert.True(r.Exito);
        Assert.Equal(2, r.Valor.SellosSumados);
        Assert.Equal(2, r.Valor.SellosActuales);
        var mov = (await NuevoContexto().Tarjetas.Include(t => t.Movimientos).SingleAsync()).Movimientos.Single();
        Assert.Equal(2, mov.Cantidad);
        Assert.Equal(25000, mov.Monto);
    }

    [Fact]
    public async Task Sin_monto_en_un_programa_por_monto_no_suma_ni_crea_la_tarjeta()
    {
        var super = await ComercioAsync("Supermercado Gómez", "supermercado-gomez", montoPorSello: 10000);
        var lucia = await ClienteAsync();

        AssertError(await Handler().Handle(new SumarSello(super.Id, lucia.Id, "caja"), default), TipoError.Validacion, "Escribí el monto de la compra.");
        Assert.Empty(await NuevoContexto().Tarjetas.ToListAsync());
    }

    [Fact]
    public async Task Sin_reglas_de_monto_suma_uno_aunque_venga_el_monto()
    {
        var cafe = await ComercioAsync();
        var lucia = await ClienteAsync();

        var r = await Handler().Handle(new SumarSello(cafe.Id, lucia.Id, "caja", 80000), default);

        Assert.Equal(1, r.Valor.SellosSumados);
    }

    [Fact]
    public async Task El_resumen_cuenta_sellos_no_compras()
    {
        var super = await ComercioAsync("Supermercado Gómez", "supermercado-gomez", montoPorSello: 10000);
        var lucia = await ClienteAsync();
        await Handler().Handle(new SumarSello(super.Id, lucia.Id, "caja", 35000), default);
        Reloj.Avanzar(TimeSpan.FromDays(1));
        await Handler().Handle(new SumarSello(super.Id, lucia.Id, "caja", 20000), default);

        var r = await new ResumenComercioHandler(Db, Reloj).Handle(new ResumenComercio(super.Id), default);

        Assert.Equal(5, r.Valor.SellosUltimos30Dias);
    }
}

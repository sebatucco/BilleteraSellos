namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// La caja suma los sellos de una compra al cliente que buscó por DNI, teléfono o el QR de su billetera.
/// Con el monto se calculan los sellos según las reglas de la tarjeta (1 por compra o 1 cada $X).
/// Si el cliente todavía no tenía la tarjeta de este comercio, se crea con esta compra.
/// </summary>
public sealed record SumarSello(int ComercioId, int ClienteId, string? CargadoPor, decimal? Monto = null) : ICommand<Resultado<SelloSumado>>;

public sealed record SelloSumado(int TarjetaId, int SellosActuales, int SellosNecesarios, bool TarjetaCompleta, Nivel Nivel, bool TarjetaNueva, int SellosSumados);

public sealed class SumarSelloHandler(IAppDbContext db, INotificador notificador, IReloj reloj) : ICommandHandler<SumarSello, Resultado<SelloSumado>>
{
    public async Task<Resultado<SelloSumado>> Handle(SumarSello c, CancellationToken ct)
    {
        var comercio = await db.Comercios.Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == c.ComercioId, ct);
        if (comercio?.Programa is not { } programa) return Error.NoEncontrado("El comercio no existe o no tiene tarjeta configurada.");
        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, ct);
        if (cliente is null) return Error.NoEncontrado("El cliente no existe.");

        var sellos = programa.SellosPorCompra(c.Monto);
        if (!sellos.Exito) return sellos.Error!;

        var ahora = reloj.UtcAhora;
        var tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.ComercioId == comercio.Id, ct);
        var nueva = tarjeta is null;
        if (tarjeta is null)
        {
            tarjeta = Tarjeta.Abrir(cliente.Id, comercio.Id, ahora);
            db.Tarjetas.Add(tarjeta);
        }

        var r = await Sellado.SumarAsync(db, notificador, tarjeta, programa, comercio, cliente, sellos.Valor, c.Monto, ahora, c.CargadoPor, ct);
        if (!r.Exito) return r.Error!;

        await db.SaveChangesAsync(ct);
        return new SelloSumado(tarjeta.Id, tarjeta.SellosActuales, programa.SellosNecesarios, tarjeta.EstaCompleta(programa), tarjeta.Nivel, nueva, sellos.Valor);
    }
}

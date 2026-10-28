namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// La caja suma un sello por una compra. Si el cliente todavía no tenía la tarjeta de este comercio, se crea con este sello.
/// Cuando la tarjeta se completa, se le avisa al cliente.
/// </summary>
public sealed record SumarSello(int ComercioId, int ClienteId, string? CargadoPor) : ICommand<Resultado<SelloSumado>>;

public sealed record SelloSumado(int TarjetaId, int SellosActuales, int SellosNecesarios, bool TarjetaCompleta, Nivel Nivel, bool TarjetaNueva);

public sealed class SumarSelloHandler(IAppDbContext db, INotificador notificador, IReloj reloj) : ICommandHandler<SumarSello, Resultado<SelloSumado>>
{
    public async Task<Resultado<SelloSumado>> Handle(SumarSello c, CancellationToken ct)
    {
        var comercio = await db.Comercios.Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == c.ComercioId, ct);
        if (comercio?.Programa is not { } programa) return Error.NoEncontrado("El comercio no existe o no tiene tarjeta configurada.");
        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, ct);
        if (cliente is null) return Error.NoEncontrado("El cliente no existe.");

        var ahora = reloj.UtcAhora;
        var tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.ComercioId == comercio.Id, ct);
        var nueva = tarjeta is null;
        if (tarjeta is null)
        {
            tarjeta = Tarjeta.Abrir(cliente.Id, comercio.Id, ahora);
            db.Tarjetas.Add(tarjeta);
        }

        var sello = tarjeta.SumarSello(programa, ahora, c.CargadoPor);
        if (!sello.Exito) return sello.Error!;

        var completa = tarjeta.EstaCompleta(programa);
        if (completa)
        {
            var mensaje = $"¡Completaste tu tarjeta en {comercio.Nombre}! Ya podés canjear: {programa.Premio}.";
            await notificador.EnviarAsync(cliente, comercio, TipoAviso.TarjetaCompleta, mensaje, ct);
            db.Avisos.Add(Aviso.Crear(cliente.Id, comercio.Id, TipoAviso.TarjetaCompleta, mensaje, ahora));
        }

        await db.SaveChangesAsync(ct);
        return new SelloSumado(tarjeta.Id, tarjeta.SellosActuales, programa.SellosNecesarios, completa, tarjeta.Nivel, nueva);
    }
}

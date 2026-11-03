namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// La caja carga los sellos que el cliente ya tenía en la tarjeta de cartón del comercio, para que no pierda lo juntado.
/// Una sola vez por tarjeta. Si el cliente todavía no tenía la tarjeta digital, se crea.
/// </summary>
public sealed record MigrarTarjetaDePapel(int ComercioId, int ClienteId, int Sellos, string? CargadoPor) : ICommand<Resultado<SelloSumado>>;

public sealed class MigrarTarjetaDePapelHandler(IAppDbContext db, INotificador notificador, IReloj reloj) : ICommandHandler<MigrarTarjetaDePapel, Resultado<SelloSumado>>
{
    public async Task<Resultado<SelloSumado>> Handle(MigrarTarjetaDePapel c, CancellationToken ct)
    {
        var comercio = await db.Comercios.Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == c.ComercioId, ct);
        if (comercio?.Programa is not { } programa) return Error.NoEncontrado("El comercio no existe o no tiene tarjeta configurada.");
        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, ct);
        if (cliente is null) return Error.NoEncontrado("El cliente no existe.");

        var ahora = reloj.UtcAhora;
        var tarjeta = await db.Tarjetas.Include(t => t.Movimientos)
            .FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.ComercioId == comercio.Id, ct);
        var nueva = tarjeta is null;
        if (tarjeta is null)
        {
            tarjeta = Tarjeta.Abrir(cliente.Id, comercio.Id, ahora);
            db.Tarjetas.Add(tarjeta);
        }

        var estabaCompleta = tarjeta.EstaCompleta(programa);
        var r = tarjeta.MigrarSellosDePapel(programa, c.Sellos, ahora, c.CargadoPor);
        if (!r.Exito) return r.Error!;

        if (!estabaCompleta && tarjeta.EstaCompleta(programa))
        {
            var mensaje = $"¡Completaste tu tarjeta en {comercio.Nombre}! Ya podés canjear: {programa.Premio}.";
            await notificador.EnviarAsync(cliente, comercio, TipoAviso.TarjetaCompleta, mensaje, ct);
            db.Avisos.Add(Aviso.Crear(cliente.Id, comercio.Id, TipoAviso.TarjetaCompleta, mensaje, ahora));
        }

        await db.SaveChangesAsync(ct);
        return new SelloSumado(tarjeta.Id, tarjeta.SellosActuales, programa.SellosNecesarios, tarjeta.EstaCompleta(programa), tarjeta.Nivel, nueva, c.Sellos);
    }
}

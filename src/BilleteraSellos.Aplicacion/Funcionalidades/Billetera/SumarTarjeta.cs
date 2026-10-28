namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>El cliente escanea el QR del mostrador y la tarjeta de ese comercio aparece en su billetera.</summary>
public sealed record SumarTarjeta(int ClienteId, string? CodigoComercio) : ICommand<Resultado<TarjetaSumada>>;

public sealed record TarjetaSumada(int TarjetaId, bool YaLaTenias);

public sealed class SumarTarjetaHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<SumarTarjeta, Resultado<TarjetaSumada>>
{
    public async Task<Resultado<TarjetaSumada>> Handle(SumarTarjeta c, CancellationToken ct)
    {
        var codigo = (c.CodigoComercio ?? "").Trim().ToLowerInvariant();
        var comercio = await db.Comercios.AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo, ct);
        if (comercio is null) return Error.NoEncontrado("No encontramos ese comercio. Revisá el código del QR.");

        var existente = await db.Tarjetas.AsNoTracking().FirstOrDefaultAsync(t => t.ClienteId == c.ClienteId && t.ComercioId == comercio.Id, ct);
        if (existente is not null) return new TarjetaSumada(existente.Id, true);

        var tarjeta = Tarjeta.Abrir(c.ClienteId, comercio.Id, reloj.UtcAhora);
        db.Tarjetas.Add(tarjeta);
        await db.SaveChangesAsync(ct);
        return new TarjetaSumada(tarjeta.Id, false);
    }
}

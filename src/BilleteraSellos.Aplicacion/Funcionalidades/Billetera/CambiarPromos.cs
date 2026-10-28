namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>El cliente decide, comercio por comercio, si quiere recibir promos.</summary>
public sealed record CambiarPromos(int ClienteId, int TarjetaId, bool Acepta) : ICommand<Resultado>;

public sealed class CambiarPromosHandler(IAppDbContext db) : ICommandHandler<CambiarPromos, Resultado>
{
    public async Task<Resultado> Handle(CambiarPromos c, CancellationToken ct)
    {
        var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == c.TarjetaId && x.ClienteId == c.ClienteId, ct);
        if (t is null) return Error.NoEncontrado("Esa tarjeta no está en tu billetera.");
        t.CambiarPromos(c.Acepta);
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}

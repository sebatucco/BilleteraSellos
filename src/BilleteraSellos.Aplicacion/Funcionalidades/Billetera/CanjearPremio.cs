namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>Canjea el premio de una tarjeta completa: descuenta los sellos y genera el código para mostrar en la caja.</summary>
public sealed record CanjearPremio(int ClienteId, int TarjetaId) : ICommand<Resultado<CanjePendiente>>;

public sealed class CanjearPremioHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<CanjearPremio, Resultado<CanjePendiente>>
{
    public async Task<Resultado<CanjePendiente>> Handle(CanjearPremio c, CancellationToken ct)
    {
        var t = await db.Tarjetas.Include(x => x.Comercio).ThenInclude(co => co!.Programa)
            .FirstOrDefaultAsync(x => x.Id == c.TarjetaId && x.ClienteId == c.ClienteId, ct);
        if (t is null) return Error.NoEncontrado("Esa tarjeta no está en tu billetera.");

        string codigo;
        do codigo = Codigos.Generar(); while (await db.Canjes.AnyAsync(k => k.Codigo == codigo, ct));

        var canje = t.Canjear(t.Comercio!.Programa!, codigo, reloj.UtcAhora);
        if (!canje.Exito) return canje.Error!;
        await db.SaveChangesAsync(ct);
        return new CanjePendiente(canje.Valor.Codigo, canje.Valor.Premio, canje.Valor.VenceEn);
    }
}

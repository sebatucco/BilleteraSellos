namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>El cliente muestra su código de canje y la caja lo marca como entregado. Solo sirve en su comercio.</summary>
public sealed record EntregarCanje(int ComercioId, string Codigo, string? EntregadoPor) : ICommand<Resultado<CanjeEntregado>>;

public sealed record CanjeEntregado(string Codigo, string Premio, string Cliente);

public sealed class EntregarCanjeHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<EntregarCanje, Resultado<CanjeEntregado>>
{
    public async Task<Resultado<CanjeEntregado>> Handle(EntregarCanje c, CancellationToken ct)
    {
        var codigo = (c.Codigo ?? "").Trim().ToUpperInvariant();
        var canje = await db.Canjes.Include(x => x.Tarjeta).ThenInclude(t => t!.Cliente)
            .FirstOrDefaultAsync(x => x.Codigo == codigo, ct);
        if (canje is null || canje.Tarjeta!.ComercioId != c.ComercioId) return Error.NoEncontrado("Ese código no existe en este comercio.");

        var entrega = canje.Entregar(reloj.UtcAhora, c.EntregadoPor);
        if (!entrega.Exito) return entrega.Error!;
        await db.SaveChangesAsync(ct);
        return new CanjeEntregado(canje.Codigo, canje.Premio, canje.Tarjeta.Cliente!.Nombre);
    }
}

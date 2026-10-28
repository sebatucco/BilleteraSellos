namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Clientes del comercio que cumplen años en los próximos días (7 por defecto, hasta 60).</summary>
public sealed record ProximosCumpleanos(int ComercioId, int? Dias) : IQuery<List<Cumpleanero>>;

public sealed record Cumpleanero(int ClienteId, string Nombre, DateOnly Fecha, int EnDias);

public sealed class ProximosCumpleanosHandler(IAppDbContext db, IReloj reloj) : IQueryHandler<ProximosCumpleanos, List<Cumpleanero>>
{
    public async Task<List<Cumpleanero>> Handle(ProximosCumpleanos q, CancellationToken ct)
    {
        var hoy = reloj.HoyLocal;
        var rango = Math.Clamp(q.Dias ?? 7, 0, 60);
        var clientes = await db.Tarjetas.AsNoTracking()
            .Where(t => t.ComercioId == q.ComercioId && t.Cliente!.FechaNacimiento != null)
            .Select(t => new { t.Cliente!.Id, t.Cliente.Nombre, Nacimiento = t.Cliente.FechaNacimiento!.Value })
            .ToListAsync(ct);
        return clientes
            .Select(c => (c, prox: Cumpleanos.Proximo(c.Nacimiento, hoy)))
            .Where(x => x.prox.DayNumber - hoy.DayNumber <= rango)
            .OrderBy(x => x.prox)
            .Select(x => new Cumpleanero(x.c.Id, x.c.Nombre, x.prox, x.prox.DayNumber - hoy.DayNumber))
            .ToList();
    }
}

namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>La billetera del cliente logueado: todas sus tarjetas, primero las que tienen premio listo o están más cerca.</summary>
public sealed record ObtenerBilletera(int ClienteId) : IQuery<Resultado<BilleteraVista>>;

public sealed record CanjePendiente(string Codigo, string Premio, DateTime VenceEn);

public sealed record TarjetaEnBilletera(
    int TarjetaId, int ComercioId, string Comercio, string Rubro, string Direccion, string Color,
    int SellosActuales, int SellosNecesarios, string Premio, bool Completa,
    Nivel Nivel, int SellosTotales, int? FaltanParaSiguienteNivel,
    bool AceptaPromos, string? ResenaGoogleUrl, List<CanjePendiente> CanjesPendientes);

public sealed record BilleteraVista(int ClienteId, string Nombre, string CodigoQr, List<TarjetaEnBilletera> Tarjetas);

public sealed class ObtenerBilleteraHandler(IAppDbContext db, IReloj reloj) : IQueryHandler<ObtenerBilletera, Resultado<BilleteraVista>>
{
    public async Task<Resultado<BilleteraVista>> Handle(ObtenerBilletera q, CancellationToken ct)
    {
        var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.ClienteId, ct);
        if (c is null) return Error.NoEncontrado("No encontramos tu cuenta.");
        var ahora = reloj.UtcAhora;
        var tarjetas = await db.Tarjetas.AsNoTracking()
            .Include(t => t.Comercio).ThenInclude(co => co!.Programa)
            .Include(t => t.Canjes)
            .Where(t => t.ClienteId == q.ClienteId)
            .ToListAsync(ct);

        var items = tarjetas
            .OrderByDescending(t => t.Canjes.Any(k => k.EstaPendiente(ahora)))
            .ThenByDescending(t => (double)t.SellosActuales / Math.Max(1, t.Comercio!.Programa!.SellosNecesarios))
            .Select(t =>
            {
                var co = t.Comercio!;
                var p = co.Programa!;
                return new TarjetaEnBilletera(t.Id, co.Id, co.Nombre, co.Rubro, co.Direccion, co.Color,
                    t.SellosActuales, p.SellosNecesarios, p.Premio, t.EstaCompleta(p),
                    t.Nivel, t.SellosTotales, Niveles.FaltanParaSiguiente(t.SellosTotales),
                    t.AceptaPromos, co.ResenaGoogleUrl,
                    t.Canjes.Where(k => k.EstaPendiente(ahora)).Select(k => new CanjePendiente(k.Codigo, k.Premio, k.VenceEn)).ToList());
            }).ToList();

        return new BilleteraVista(c.Id, c.Nombre, c.CodigoQr, items);
    }
}

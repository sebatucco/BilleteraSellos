namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

public sealed record ProgramaVista(int SellosNecesarios, string Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos,
    decimal? MontoPorSello, decimal? CompraMinima, string? ResenaGoogleUrl, int? PedirResenaTrasVisitas)
{
    public static ProgramaVista Desde(ProgramaSellos p, string? resenaGoogleUrl) =>
        new(p.SellosNecesarios, p.Premio, p.CostoPremio, p.DiasParaRetirar, p.HorasEntreSellos, p.MontoPorSello, p.CompraMinima,
            resenaGoogleUrl, p.PedirResenaTrasVisitas);
}

/// <summary>Las reglas de la tarjeta del comercio.</summary>
public sealed record ObtenerPrograma(int ComercioId) : IQuery<Resultado<ProgramaVista>>;

public sealed class ObtenerProgramaHandler(IAppDbContext db) : IQueryHandler<ObtenerPrograma, Resultado<ProgramaVista>>
{
    public async Task<Resultado<ProgramaVista>> Handle(ObtenerPrograma q, CancellationToken ct)
    {
        var co = await db.Comercios.AsNoTracking().Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == q.ComercioId, ct);
        return co?.Programa is null ? Error.NoEncontrado("El comercio no tiene tarjeta configurada.") : ProgramaVista.Desde(co.Programa, co.ResenaGoogleUrl);
    }
}

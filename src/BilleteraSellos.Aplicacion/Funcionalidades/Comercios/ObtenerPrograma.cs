namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

public sealed record ProgramaVista(int SellosNecesarios, string Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos)
{
    public static ProgramaVista Desde(ProgramaSellos p) => new(p.SellosNecesarios, p.Premio, p.CostoPremio, p.DiasParaRetirar, p.HorasEntreSellos);
}

/// <summary>Las reglas de la tarjeta del comercio.</summary>
public sealed record ObtenerPrograma(int ComercioId) : IQuery<Resultado<ProgramaVista>>;

public sealed class ObtenerProgramaHandler(IAppDbContext db) : IQueryHandler<ObtenerPrograma, Resultado<ProgramaVista>>
{
    public async Task<Resultado<ProgramaVista>> Handle(ObtenerPrograma q, CancellationToken ct)
    {
        var p = await db.Programas.AsNoTracking().FirstOrDefaultAsync(x => x.ComercioId == q.ComercioId, ct);
        return p is null ? Error.NoEncontrado("El comercio no tiene tarjeta configurada.") : ProgramaVista.Desde(p);
    }
}

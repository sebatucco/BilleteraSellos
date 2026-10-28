namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Directorio público de comercios adheridos (lo usa la billetera para "cerca tuyo").</summary>
public sealed record DirectorioComercios : IQuery<List<ComercioResumido>>;

public sealed record ComercioResumido(int Id, string Nombre, string Rubro, string Direccion, string Codigo, string Color, string? Premio, int? SellosNecesarios);

public sealed class DirectorioComerciosHandler(IAppDbContext db) : IQueryHandler<DirectorioComercios, List<ComercioResumido>>
{
    public Task<List<ComercioResumido>> Handle(DirectorioComercios q, CancellationToken ct) =>
        db.Comercios.AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new ComercioResumido(c.Id, c.Nombre, c.Rubro, c.Direccion, c.Codigo, c.Color,
                c.Programa != null ? c.Programa.Premio : null,
                c.Programa != null ? (int?)c.Programa.SellosNecesarios : null))
            .ToListAsync(ct);
}

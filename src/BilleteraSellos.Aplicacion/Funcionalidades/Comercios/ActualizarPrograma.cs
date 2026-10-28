namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>El dueño cambia las reglas de la tarjeta.</summary>
public sealed record ActualizarPrograma(int ComercioId, int SellosNecesarios, string? Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos)
    : ICommand<Resultado<ProgramaVista>>;

public sealed class ActualizarProgramaHandler(IAppDbContext db) : ICommandHandler<ActualizarPrograma, Resultado<ProgramaVista>>
{
    public async Task<Resultado<ProgramaVista>> Handle(ActualizarPrograma c, CancellationToken ct)
    {
        var p = await db.Programas.FirstOrDefaultAsync(x => x.ComercioId == c.ComercioId, ct);
        if (p is null) return Error.NoEncontrado("El comercio no tiene tarjeta configurada.");
        var r = p.Actualizar(c.SellosNecesarios, c.Premio, c.CostoPremio, c.DiasParaRetirar, c.HorasEntreSellos);
        if (!r.Exito) return r.Error!;
        await db.SaveChangesAsync(ct);
        return ProgramaVista.Desde(p);
    }
}

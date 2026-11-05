namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// El dueño cambia las reglas de la tarjeta y el pedido de reseñas en Google.
/// <paramref name="ResenaGoogleUrl"/>: null = no cambia, vacío = se borra. <paramref name="PedirResenaTrasVisitas"/>: null = no se pide.
/// </summary>
public sealed record ActualizarPrograma(int ComercioId, int SellosNecesarios, string? Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos,
    decimal? MontoPorSello = null, decimal? CompraMinima = null, string? ResenaGoogleUrl = null, int? PedirResenaTrasVisitas = null)
    : ICommand<Resultado<ProgramaVista>>;

public sealed class ActualizarProgramaHandler(IAppDbContext db) : ICommandHandler<ActualizarPrograma, Resultado<ProgramaVista>>
{
    public async Task<Resultado<ProgramaVista>> Handle(ActualizarPrograma c, CancellationToken ct)
    {
        var co = await db.Comercios.Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == c.ComercioId, ct);
        if (co?.Programa is not { } p) return Error.NoEncontrado("El comercio no tiene tarjeta configurada.");
        // Primero las reseñas: si el link no es válido no se cambia nada.
        var resenas = co.ConfigurarResenas(c.ResenaGoogleUrl, c.PedirResenaTrasVisitas);
        if (!resenas.Exito) return resenas.Error!;
        var r = p.Actualizar(c.SellosNecesarios, c.Premio, c.CostoPremio, c.DiasParaRetirar, c.HorasEntreSellos, c.MontoPorSello, c.CompraMinima);
        if (!r.Exito) return r.Error!;
        await db.SaveChangesAsync(ct);
        return ProgramaVista.Desde(p, co.ResenaGoogleUrl);
    }
}

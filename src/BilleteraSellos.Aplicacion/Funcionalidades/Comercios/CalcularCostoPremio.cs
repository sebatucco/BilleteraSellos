namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Cuánto cuesta la tarjeta por mes. Sin sellos/costo usa los del programa actual del comercio.</summary>
public sealed record CalcularCostoPremio(int ComercioId, decimal TicketPromedio, int ComprasConSelloPorMes, int? SellosNecesarios, decimal? CostoPremio)
    : IQuery<Resultado<CostoEstimado>>;

public sealed class CalcularCostoPremioHandler(IAppDbContext db) : IQueryHandler<CalcularCostoPremio, Resultado<CostoEstimado>>
{
    public async Task<Resultado<CostoEstimado>> Handle(CalcularCostoPremio q, CancellationToken ct)
    {
        var p = await db.Programas.AsNoTracking().FirstOrDefaultAsync(x => x.ComercioId == q.ComercioId, ct);
        if (p is null && (q.SellosNecesarios is null || q.CostoPremio is null))
            return Error.NoEncontrado("El comercio no tiene programa; pasá sellosNecesarios y costoPremio.");
        var sellos = q.SellosNecesarios ?? p!.SellosNecesarios;
        if (sellos < 1) return Error.Validacion("sellosNecesarios tiene que ser al menos 1.");
        return CalculadoraCosto.Calcular(sellos, q.CostoPremio ?? p!.CostoPremio, q.TicketPromedio, q.ComprasConSelloPorMes);
    }
}

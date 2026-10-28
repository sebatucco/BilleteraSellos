namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Números del panel: clientes, sellos del último mes, clientes en riesgo y premios por entregar.</summary>
public sealed record ResumenComercio(int ComercioId) : IQuery<Resultado<Resumen>>;

public sealed record Resumen(int Clientes, int SellosUltimos30Dias, int ClientesEnRiesgo, int CanjesPendientes);

public sealed class ResumenComercioHandler(IAppDbContext db, IReloj reloj) : IQueryHandler<ResumenComercio, Resultado<Resumen>>
{
    public async Task<Resultado<Resumen>> Handle(ResumenComercio q, CancellationToken ct)
    {
        if (!await db.Comercios.AnyAsync(c => c.Id == q.ComercioId, ct)) return Error.NoEncontrado("El comercio no existe.");
        var ahora = reloj.UtcAhora;
        var desde = ahora.AddDays(-30);
        var tarjetas = await db.Tarjetas.AsNoTracking()
            .Include(t => t.Movimientos).Include(t => t.Canjes)
            .Where(t => t.ComercioId == q.ComercioId)
            .ToListAsync(ct);

        return new Resumen(
            tarjetas.Count,
            tarjetas.Sum(t => t.Movimientos.Count(m => m.Tipo == TipoMovimiento.Sello && m.Fecha >= desde)),
            tarjetas.Count(t => Enfriamiento.EstaEnfriandose(Enfriamiento.Evaluar(t.Visitas(), ahora))),
            tarjetas.Sum(t => t.Canjes.Count(c => c.EstaPendiente(ahora))));
    }
}

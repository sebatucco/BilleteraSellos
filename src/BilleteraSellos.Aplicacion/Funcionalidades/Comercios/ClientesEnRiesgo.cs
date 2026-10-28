namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Clientes que vienen menos que su ritmo habitual, los más urgentes primero.</summary>
public sealed record ClientesEnRiesgo(int ComercioId) : IQuery<List<ClienteEnfriandose>>;

public sealed record ClienteEnfriandose(int ClienteId, string Nombre, string Telefono, int DiasSinVenir, double? CadaCuantosDias, EstadoCliente Estado, bool AceptaPromos);

public sealed class ClientesEnRiesgoHandler(IAppDbContext db, IReloj reloj) : IQueryHandler<ClientesEnRiesgo, List<ClienteEnfriandose>>
{
    public async Task<List<ClienteEnfriandose>> Handle(ClientesEnRiesgo q, CancellationToken ct)
    {
        var ahora = reloj.UtcAhora;
        var tarjetas = await db.Tarjetas.AsNoTracking()
            .Include(t => t.Cliente).Include(t => t.Movimientos)
            .Where(t => t.ComercioId == q.ComercioId)
            .ToListAsync(ct);
        return tarjetas
            .Select(t => (t, r: Enfriamiento.Evaluar(t.Visitas(), ahora)))
            .Where(x => Enfriamiento.EstaEnfriandose(x.r))
            .OrderByDescending(x => x.r.Relacion)
            .Select(x => new ClienteEnfriandose(x.t.ClienteId, x.t.Cliente!.Nombre, x.t.Cliente.Telefono, x.r.DiasSinVenir, x.r.CadaCuantosDias, x.r.Estado, x.t.AceptaPromos))
            .ToList();
    }
}

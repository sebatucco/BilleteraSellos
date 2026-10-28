namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>Busca en la caja por DNI, teléfono (con o sin +54 9, 0 y 15) o el código del QR de la billetera.</summary>
public sealed record BuscarClienteEnCaja(int ComercioId, string Texto) : IQuery<Resultado<ClienteEnCaja>>;

public sealed record ClienteEnCaja(int ClienteId, string Nombre, bool TieneTarjeta, int SellosActuales, int SellosNecesarios, Nivel Nivel, bool PremioDisponible);

public sealed class BuscarClienteEnCajaHandler(IAppDbContext db) : IQueryHandler<BuscarClienteEnCaja, Resultado<ClienteEnCaja>>
{
    public async Task<Resultado<ClienteEnCaja>> Handle(BuscarClienteEnCaja q, CancellationToken ct)
    {
        var programa = await db.Programas.AsNoTracking().FirstOrDefaultAsync(p => p.ComercioId == q.ComercioId, ct);
        if (programa is null) return Error.NoEncontrado("El comercio no existe o no tiene tarjeta configurada.");

        var cliente = await Buscar(db, q.Texto, ct);
        if (cliente is null) return Error.NoEncontrado("No hay ningún cliente con ese DNI, teléfono o QR. Registralo como cliente nuevo.");

        var t = await db.Tarjetas.AsNoTracking().FirstOrDefaultAsync(x => x.ClienteId == cliente.Id && x.ComercioId == q.ComercioId, ct);
        return new ClienteEnCaja(cliente.Id, cliente.Nombre, t is not null, t?.SellosActuales ?? 0, programa.SellosNecesarios,
            Niveles.Calcular(t?.SellosTotales ?? 0), t is not null && t.EstaCompleta(programa));
    }

    internal static Task<Cliente?> Buscar(IAppDbContext db, string texto, CancellationToken ct)
    {
        var limpio = (texto ?? "").Trim();
        var digitos = Codigos.SoloDigitos(limpio);
        var tel = Telefonos.Normalizar(limpio);
        var porNumero = digitos.Length >= 6;
        return db.Clientes.AsNoTracking().FirstOrDefaultAsync(c =>
            c.CodigoQr == limpio || (porNumero && (c.Dni == digitos || c.Telefono == tel)), ct);
    }
}

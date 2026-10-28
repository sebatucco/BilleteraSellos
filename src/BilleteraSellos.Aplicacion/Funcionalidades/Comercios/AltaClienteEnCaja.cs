namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// Alta en la caja para quien no tiene la app (alcanza con DNI y celular).
/// Cuando después entre con su teléfono por SMS, encuentra esta misma cuenta con sus sellos.
/// </summary>
public sealed record AltaClienteEnCaja(int ComercioId, string? Nombre, string? Dni, string? Telefono, DateOnly? FechaNacimiento)
    : ICommand<Resultado<ClienteEnCaja>>;

public sealed class AltaClienteEnCajaHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<AltaClienteEnCaja, Resultado<ClienteEnCaja>>
{
    public async Task<Resultado<ClienteEnCaja>> Handle(AltaClienteEnCaja c, CancellationToken ct)
    {
        var tel = Telefonos.Normalizar(c.Telefono);
        var alta = Cliente.Registrar(c.Nombre, c.Dni, tel, c.FechaNacimiento, reloj.HoyLocal);
        if (!alta.Exito) return alta.Error!;
        var cliente = alta.Valor;
        if (await db.Clientes.AnyAsync(x => x.Dni == cliente.Dni || x.Telefono == cliente.Telefono, ct))
            return Error.Conflicto("Ya hay un cliente con ese DNI o teléfono. Buscalo y sumale el sello.");

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);
        var programa = await db.Programas.AsNoTracking().FirstOrDefaultAsync(x => x.ComercioId == c.ComercioId, ct);
        return new ClienteEnCaja(cliente.Id, cliente.Nombre, false, 0, programa?.SellosNecesarios ?? 0, Nivel.Bronce, false);
    }
}

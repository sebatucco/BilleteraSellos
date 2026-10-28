namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>
/// Paso final del alta desde la billetera. El teléfono sale de la sesión de registro (ya verificado por SMS),
/// no del pedido. Cierra la sesión de registro y abre la del cliente.
/// </summary>
public sealed record RegistrarCliente(string Telefono, int? SesionRegistroId, string? Nombre, string? Dni, DateOnly? FechaNacimiento)
    : ICommand<Resultado<ClienteRegistrado>>;

public sealed record ClienteRegistrado(int Id, string Nombre, string Token, DateTime VenceEn);

public sealed class RegistrarClienteHandler(IAppDbContext db, ServicioSesiones sesiones, IReloj reloj)
    : ICommandHandler<RegistrarCliente, Resultado<ClienteRegistrado>>
{
    public async Task<Resultado<ClienteRegistrado>> Handle(RegistrarCliente c, CancellationToken ct)
    {
        var alta = Cliente.Registrar(c.Nombre, c.Dni, c.Telefono ?? "", c.FechaNacimiento, reloj.HoyLocal);
        if (!alta.Exito) return alta.Error!;
        var cliente = alta.Valor;
        if (await db.Clientes.AnyAsync(x => x.Dni == cliente.Dni || x.Telefono == cliente.Telefono, ct))
            return Error.Conflicto("Ya hay una cuenta con ese DNI o teléfono.");

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync(ct);

        if (c.SesionRegistroId is { } registro) await sesiones.CerrarAsync(registro, ct);
        var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: cliente.Id, ct: ct);
        return new ClienteRegistrado(cliente.Id, cliente.Nombre, token, vence);
    }
}

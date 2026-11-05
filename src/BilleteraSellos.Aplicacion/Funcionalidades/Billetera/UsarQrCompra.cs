using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>
/// El cliente escanea el QR de la compra con su billetera y se le suman los sellos.
/// Si todavía no tenía la tarjeta de ese comercio, se crea. Si la regla de horas entre sellos no lo deja,
/// el QR NO se consume y el cliente puede usarlo más tarde mientras no venza.
/// </summary>
public sealed record UsarQrCompra(int ClienteId, string? Codigo) : ICommand<Resultado<SellosDeCompra>>;

public sealed record SellosDeCompra(string Comercio, int ComercioId, int SellosSumados, int SellosActuales, int SellosNecesarios, bool TarjetaCompleta, bool TarjetaNueva);

public sealed class UsarQrCompraHandler(IAppDbContext db, INotificador notificador, IReloj reloj) : ICommandHandler<UsarQrCompra, Resultado<SellosDeCompra>>
{
    public async Task<Resultado<SellosDeCompra>> Handle(UsarQrCompra c, CancellationToken ct)
    {
        var compra = await ComprasQr.BuscarAsync(db, c.Codigo, ct);
        if (compra is null) return ComprasQr.NoExiste;
        var ahora = reloj.UtcAhora;
        if (compra.PuedeUsarse(ahora) is { Exito: false } no) return no.Error!;

        var comercio = await db.Comercios.Include(x => x.Programa).FirstOrDefaultAsync(x => x.Id == compra.ComercioId, ct);
        if (comercio?.Programa is not { } programa) return Error.NoEncontrado("El comercio no existe o no tiene tarjeta configurada.");
        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Id == c.ClienteId, ct);
        if (cliente is null) return Error.NoEncontrado("El cliente no existe.");
        var cajero = await db.Usuarios.AsNoTracking().Where(u => u.Id == compra.UsuarioId).Select(u => u.Nombre).FirstOrDefaultAsync(ct);

        var tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.ComercioId == comercio.Id, ct);
        var nueva = tarjeta is null;
        if (tarjeta is null)
        {
            tarjeta = Tarjeta.Abrir(cliente.Id, comercio.Id, ahora);
            db.Tarjetas.Add(tarjeta);
        }

        // Primero los sellos: si la regla de horas no lo permite, no se guarda nada y el QR sigue sirviendo.
        var r = await Sellado.SumarAsync(db, notificador, tarjeta, programa, comercio, cliente, compra.Sellos, compra.Monto, ahora, cajero, ct);
        if (!r.Exito) return r.Error!;
        compra.Usar(cliente.Id, ahora);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dos escaneos casi al mismo tiempo: la base deja pasar solo uno (UsadaEn es token de concurrencia).
            return Error.Conflicto("Ese QR ya se usó.");
        }

        return new SellosDeCompra(comercio.Nombre, comercio.Id, compra.Sellos, tarjeta.SellosActuales, programa.SellosNecesarios, tarjeta.EstaCompleta(programa), nueva);
    }
}

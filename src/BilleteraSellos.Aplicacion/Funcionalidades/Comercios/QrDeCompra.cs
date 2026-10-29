namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// La caja escribe el monto y genera el QR de la compra. El cliente lo escanea con su billetera (cualquier medio de pago).
/// Los sellos se calculan acá, con las reglas vigentes, y quedan fijos en el QR.
/// </summary>
public sealed record GenerarQrCompra(int ComercioId, int UsuarioId, decimal? Monto) : ICommand<Resultado<QrCompraGenerado>>;

/// <summary>El código va dentro del QR (en el link {origen}/#compra={codigo}). Solo se devuelve acá: en la base queda su hash.</summary>
public sealed record QrCompraGenerado(string Codigo, int Sellos, decimal? Monto, DateTime VenceEn);

public sealed class GenerarQrCompraHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<GenerarQrCompra, Resultado<QrCompraGenerado>>
{
    public async Task<Resultado<QrCompraGenerado>> Handle(GenerarQrCompra c, CancellationToken ct)
    {
        var programa = await db.Programas.AsNoTracking().FirstOrDefaultAsync(x => x.ComercioId == c.ComercioId, ct);
        if (programa is null) return Error.NoEncontrado("El comercio no tiene tarjeta configurada.");
        var sellos = programa.SellosPorCompra(c.Monto);
        if (!sellos.Exito) return sellos.Error!;

        var codigo = Tokens.Generar();
        var compra = CompraQr.Generar(c.ComercioId, c.UsuarioId, Tokens.Hash(codigo), c.Monto, sellos.Valor, reloj.UtcAhora);
        db.ComprasQr.Add(compra);
        await db.SaveChangesAsync(ct);
        return new QrCompraGenerado(codigo, compra.Sellos, compra.Monto, compra.VenceEn);
    }
}

/// <summary>La pantalla de la caja pregunta cada pocos segundos si el cliente ya escaneó el QR.</summary>
public sealed record ConsultarQrCompra(int ComercioId, string? Codigo) : IQuery<Resultado<QrCompraVista>>;

/// <summary>Cliente: nombre de pila e inicial del apellido de quien lo usó ("Lucía F."), solo si ya se usó.</summary>
public sealed record QrCompraVista(EstadoCompraQr Estado, int Sellos, decimal? Monto, DateTime VenceEn, string? Cliente);

public sealed class ConsultarQrCompraHandler(IAppDbContext db, IReloj reloj) : IQueryHandler<ConsultarQrCompra, Resultado<QrCompraVista>>
{
    public async Task<Resultado<QrCompraVista>> Handle(ConsultarQrCompra q, CancellationToken ct)
    {
        var compra = await ComprasQr.BuscarAsync(db, q.Codigo, ct, sinSeguimiento: true);
        if (compra is null || compra.ComercioId != q.ComercioId) return ComprasQr.NoExiste;

        string? cliente = null;
        if (compra.ClienteId is { } clienteId)
        {
            var nombre = await db.Clientes.AsNoTracking().Where(x => x.Id == clienteId).Select(x => x.Nombre).FirstOrDefaultAsync(ct);
            cliente = nombre is null ? null : Sellado.NombreCorto(nombre);
        }
        return new QrCompraVista(compra.Estado(reloj.UtcAhora), compra.Sellos, compra.Monto, compra.VenceEn, cliente);
    }
}

/// <summary>La caja anula un QR que todavía no se usó (por ejemplo, se equivocó en el monto).</summary>
public sealed record AnularQrCompra(int ComercioId, string? Codigo) : ICommand<Resultado>;

public sealed class AnularQrCompraHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<AnularQrCompra, Resultado>
{
    public async Task<Resultado> Handle(AnularQrCompra c, CancellationToken ct)
    {
        var compra = await ComprasQr.BuscarAsync(db, c.Codigo, ct);
        if (compra is null || compra.ComercioId != c.ComercioId) return ComprasQr.NoExiste;
        var r = compra.Anular(reloj.UtcAhora);
        if (!r.Exito) return r;
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}

/// <summary>Búsqueda del QR por el hash de su código, compartida por la caja y la billetera.</summary>
internal static class ComprasQr
{
    public static readonly Error NoExiste = Error.NoEncontrado("Ese QR no existe.");

    public static async Task<CompraQr?> BuscarAsync(IAppDbContext db, string? codigo, CancellationToken ct, bool sinSeguimiento = false)
    {
        var texto = codigo?.Trim() ?? "";
        // Los códigos son de 43 caracteres: algo muy distinto ni se busca.
        if (texto.Length is < 20 or > 100) return null;
        var hash = Tokens.Hash(texto);
        var consulta = sinSeguimiento ? db.ComprasQr.AsNoTracking() : db.ComprasQr;
        return await consulta.FirstOrDefaultAsync(x => x.CodigoHash == hash, ct);
    }
}

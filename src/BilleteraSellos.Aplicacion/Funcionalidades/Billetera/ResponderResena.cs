namespace BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

/// <summary>
/// El cliente contesta el pedido de reseña de Google ("Ya la dejé" o "Ahora no") y el recuadro deja de aparecer en su billetera.
/// No se verifica si de verdad la dejó: la reseña no da sellos ni premios.
/// </summary>
public sealed record ResponderResena(int ClienteId, int TarjetaId, string? Respuesta) : ICommand<Resultado>;

public sealed class ResponderResenaHandler(IAppDbContext db, IReloj reloj) : ICommandHandler<ResponderResena, Resultado>
{
    public async Task<Resultado> Handle(ResponderResena c, CancellationToken ct)
    {
        if (!Enum.TryParse<RespuestaResena>(c.Respuesta?.Trim(), ignoreCase: true, out var respuesta) || !Enum.IsDefined(respuesta))
            return Error.Validacion("La respuesta tiene que ser Dejada o AhoraNo.");
        var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == c.TarjetaId && x.ClienteId == c.ClienteId, ct);
        if (t is null) return Error.NoEncontrado("Esa tarjeta no está en tu billetera.");
        var r = t.ResponderResena(respuesta, reloj.UtcAhora);
        if (!r.Exito) return r.Error!;
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}

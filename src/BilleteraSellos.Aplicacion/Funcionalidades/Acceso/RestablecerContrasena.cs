namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Crea la contraseña nueva con el link del email: desbloquea la cuenta y cierra todas las sesiones.</summary>
public sealed record RestablecerContrasena(string? Token, string? Nueva) : ICommand<Resultado<RespuestaMensaje>>;

public sealed class RestablecerContrasenaHandler(IAppDbContext db, IHasherContrasenas hasher, ServicioSesiones sesiones, IReloj reloj)
    : ICommandHandler<RestablecerContrasena, Resultado<RespuestaMensaje>>
{
    const string Invalido = "El link no es válido o ya venció. Pedí uno nuevo desde \"¿Olvidaste tu contraseña?\".";

    public async Task<Resultado<RespuestaMensaje>> Handle(RestablecerContrasena c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Token) || c.Token.Length > 100) return Error.Validacion(Invalido);
        var ahora = reloj.UtcAhora;
        var hash = Tokens.Hash(c.Token.Trim());
        var t = await db.TokensRestablecimiento.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (t is null || !t.EstaVigente(ahora)) return Error.Validacion(Invalido);
        if (PoliticaContrasenas.Validar(c.Nueva) is { } debil) return Error.Validacion(debil);

        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == t.UsuarioId, ct);
        if (u is null || !u.Activo) return Error.Validacion(Invalido);

        u.Restablecer(hasher.Hashear(c.Nueva!));
        t.Usar(ahora);
        await db.SaveChangesAsync(ct);
        await sesiones.CerrarTodasDeUsuarioAsync(u.Id, ct: ct);
        return new RespuestaMensaje("Listo, ya podés ingresar con tu contraseña nueva.");
    }
}

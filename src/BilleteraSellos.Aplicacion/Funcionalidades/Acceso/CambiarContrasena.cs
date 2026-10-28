namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Cambia la contraseña del usuario logueado y cierra sus otras sesiones (por si alguien más la sabía).</summary>
public sealed record CambiarContrasena(int UsuarioId, int? SesionActualId, string? Actual, string? Nueva) : ICommand<Resultado<RespuestaMensaje>>;

public sealed class CambiarContrasenaHandler(IAppDbContext db, IHasherContrasenas hasher, ServicioSesiones sesiones)
    : ICommandHandler<CambiarContrasena, Resultado<RespuestaMensaje>>
{
    public async Task<Resultado<RespuestaMensaje>> Handle(CambiarContrasena c, CancellationToken ct)
    {
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == c.UsuarioId, ct);
        if (u is null) return Error.NoAutorizado("Tenés que iniciar sesión.");
        if (!hasher.Verificar(c.Actual ?? "", u.PasswordHash)) return Error.Validacion("La contraseña actual no es correcta.");
        if (PoliticaContrasenas.Validar(c.Nueva) is { } debil) return Error.Validacion(debil);
        if (c.Nueva == c.Actual) return Error.Validacion("La contraseña nueva tiene que ser distinta de la actual.");

        u.CambiarContrasena(hasher.Hashear(c.Nueva!));
        await db.SaveChangesAsync(ct);
        await sesiones.CerrarTodasDeUsuarioAsync(u.Id, salvo: c.SesionActualId, ct: ct);
        return new RespuestaMensaje("Listo, cambiaste tu contraseña. Cerramos tus sesiones en otros dispositivos.");
    }
}

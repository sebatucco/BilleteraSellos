namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Login del panel con email y contraseña. Cinco fallos seguidos bloquean la cuenta 15 minutos.</summary>
public sealed record LoginComercio(string? Email, string? Contrasena) : ICommand<Resultado<SesionComercio>>;

public sealed class LoginComercioHandler(IAppDbContext db, IHasherContrasenas hasher, ServicioSesiones sesiones, IReloj reloj)
    : ICommandHandler<LoginComercio, Resultado<SesionComercio>>
{
    const string Incorrecto = "Email o contraseña incorrectos.";
    // Hash de una contraseña inexistente: se verifica igual cuando el email no existe, para no revelar
    // por el tiempo de respuesta qué emails están registrados.
    static string? _hashFicticio;

    public async Task<Resultado<SesionComercio>> Handle(LoginComercio c, CancellationToken ct)
    {
        var email = Emails.Normalizar(c.Email);
        var u = email is null ? null : await db.Usuarios.Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Email == email, ct);
        if (u is null)
        {
            hasher.Verificar(c.Contrasena ?? "", _hashFicticio ??= hasher.Hashear(Guid.NewGuid().ToString()));
            return Error.NoAutorizado(Incorrecto);
        }

        var ahora = reloj.UtcAhora;
        if (u.EstaBloqueado(ahora))
            return Error.DemasiadosIntentos($"Demasiados intentos fallidos. Probá de nuevo en {Math.Ceiling((u.BloqueadoHasta!.Value - ahora).TotalMinutes)} minutos.");

        if (!hasher.Verificar(c.Contrasena ?? "", u.PasswordHash))
        {
            u.RegistrarFalla(ahora);
            await db.SaveChangesAsync(ct);
            return Error.NoAutorizado(Incorrecto);
        }
        if (!u.Activo) return Error.Prohibido("Tu usuario está desactivado. Hablá con el dueño del comercio.");

        u.RegistrarIngreso();
        await db.SaveChangesAsync(ct);
        var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id, ct: ct);
        return new SesionComercio(token, vence, u.Id, u.Nombre, u.Rol, u.ComercioId, u.Comercio!.Nombre);
    }
}

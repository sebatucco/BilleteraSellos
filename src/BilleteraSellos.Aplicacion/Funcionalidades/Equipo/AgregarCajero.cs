namespace BilleteraSellos.Aplicacion.Funcionalidades.Equipo;

/// <summary>El dueño agrega un cajero a su comercio.</summary>
public sealed record AgregarCajero(int ComercioId, string? Nombre, string? Email, string? Contrasena) : ICommand<Resultado<UsuarioVista>>;

public sealed class AgregarCajeroHandler(IAppDbContext db, IHasherContrasenas hasher) : ICommandHandler<AgregarCajero, Resultado<UsuarioVista>>
{
    public async Task<Resultado<UsuarioVista>> Handle(AgregarCajero c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Nombre)) return Error.Validacion("Falta el nombre.");
        var email = Emails.Normalizar(c.Email);
        if (email is null) return Error.Validacion("El email no es válido.");
        if (PoliticaContrasenas.Validar(c.Contrasena) is { } debil) return Error.Validacion(debil);
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct)) return Error.Conflicto("Ya hay un usuario con ese email.");

        var u = Usuario.Crear(c.ComercioId, c.Nombre, email, hasher.Hashear(c.Contrasena!), RolUsuario.Cajero);
        db.Usuarios.Add(u);
        await db.SaveChangesAsync(ct);
        return UsuarioVista.Desde(u);
    }
}

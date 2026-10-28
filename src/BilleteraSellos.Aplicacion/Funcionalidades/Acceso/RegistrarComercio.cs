namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Alta de un comercio nuevo con su dueño (prueba gratis, sin intervención). Devuelve la sesión del dueño.</summary>
public sealed record RegistrarComercio(string? NombreComercio, string? Rubro, string? Direccion, string? NombreDueno, string? Email, string? Contrasena)
    : ICommand<Resultado<SesionComercio>>;

public sealed record SesionComercio(string Token, DateTime VenceEn, int UsuarioId, string Nombre, RolUsuario Rol, int ComercioId, string Comercio);

public sealed class RegistrarComercioHandler(IAppDbContext db, IHasherContrasenas hasher, ServicioSesiones sesiones)
    : ICommandHandler<RegistrarComercio, Resultado<SesionComercio>>
{
    public async Task<Resultado<SesionComercio>> Handle(RegistrarComercio c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.NombreComercio) || string.IsNullOrWhiteSpace(c.NombreDueno))
            return Error.Validacion("Falta el nombre del comercio o del dueño.");
        var email = Emails.Normalizar(c.Email);
        if (email is null) return Error.Validacion("El email no es válido.");
        if (PoliticaContrasenas.Validar(c.Contrasena) is { } debil) return Error.Validacion(debil);
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct)) return Error.Conflicto("Ya hay una cuenta con ese email. Iniciá sesión.");

        var programa = ProgramaSellos.Nuevo(10, "Premio a definir", 0);
        var comercio = Comercio.Crear(c.NombreComercio, c.Rubro ?? "", c.Direccion ?? "", await CodigoUnicoAsync(c.NombreComercio, ct), null, programa);
        var dueno = Usuario.Crear(comercio, c.NombreDueno, email, hasher.Hashear(c.Contrasena!), RolUsuario.Dueno);
        db.Comercios.Add(comercio);
        db.Usuarios.Add(dueno);
        await db.SaveChangesAsync(ct);

        var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: dueno.Id, ct: ct);
        return new SesionComercio(token, vence, dueno.Id, dueno.Nombre, dueno.Rol, comercio.Id, comercio.Nombre);
    }

    /// <summary>"Café Tostado" → "cafe-tostado"; si ya existe, "cafe-tostado-2", "-3"...</summary>
    async Task<string> CodigoUnicoAsync(string nombre, CancellationToken ct)
    {
        var baseCodigo = CodigosComercio.Base(nombre);
        var codigo = baseCodigo;
        for (var n = 2; await db.Comercios.AnyAsync(x => x.Codigo == codigo, ct); n++) codigo = $"{baseCodigo}-{n}";
        return codigo;
    }
}

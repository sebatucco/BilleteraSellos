namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Quién es el dueño del token. Devuelve una de las tres identidades según el tipo de sesión.</summary>
public sealed record QuienSoy(TipoSesion Tipo, int? UsuarioId, int? ClienteId, string? Telefono) : IQuery<Resultado<object>>;

public sealed record IdentidadComercio(string Tipo, int UsuarioId, string Nombre, string Rol, int ComercioId, string Comercio);
public sealed record IdentidadCliente(string Tipo, int ClienteId, string Nombre);
public sealed record IdentidadRegistro(string Tipo, string? Telefono);

public sealed class QuienSoyHandler(IAppDbContext db) : IQueryHandler<QuienSoy, Resultado<object>>
{
    public async Task<Resultado<object>> Handle(QuienSoy q, CancellationToken ct)
    {
        var tipo = q.Tipo.ToString();
        switch (q.Tipo)
        {
            case TipoSesion.Comercio:
                var u = await db.Usuarios.AsNoTracking().Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Id == q.UsuarioId, ct);
                if (u is null) return Error.NoAutorizado("Tenés que iniciar sesión.");
                return new IdentidadComercio(tipo, u.Id, u.Nombre, u.Rol.ToString(), u.ComercioId, u.Comercio!.Nombre);
            case TipoSesion.Cliente:
                var c = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.ClienteId, ct);
                if (c is null) return Error.NoAutorizado("Tenés que iniciar sesión.");
                return new IdentidadCliente(tipo, c.Id, c.Nombre);
            default:
                return new IdentidadRegistro(tipo, q.Telefono);
        }
    }
}

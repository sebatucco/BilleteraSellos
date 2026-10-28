namespace BilleteraSellos.Aplicacion.Funcionalidades.Equipo;

/// <summary>Usuarios del panel de un comercio: primero el dueño, después los cajeros por nombre.</summary>
public sealed record ListarEquipo(int ComercioId) : IQuery<List<UsuarioVista>>;

public sealed record UsuarioVista(int Id, string Nombre, string Email, RolUsuario Rol, bool Activo)
{
    public static UsuarioVista Desde(Usuario u) => new(u.Id, u.Nombre, u.Email, u.Rol, u.Activo);
}

public sealed class ListarEquipoHandler(IAppDbContext db) : IQueryHandler<ListarEquipo, List<UsuarioVista>>
{
    public Task<List<UsuarioVista>> Handle(ListarEquipo q, CancellationToken ct) =>
        db.Usuarios.AsNoTracking()
            .Where(u => u.ComercioId == q.ComercioId)
            .OrderBy(u => u.Rol).ThenBy(u => u.Nombre)
            .Select(u => new UsuarioVista(u.Id, u.Nombre, u.Email, u.Rol, u.Activo))
            .ToListAsync(ct);
}

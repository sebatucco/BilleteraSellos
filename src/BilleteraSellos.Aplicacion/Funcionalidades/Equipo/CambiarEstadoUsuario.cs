namespace BilleteraSellos.Aplicacion.Funcionalidades.Equipo;

/// <summary>El dueño activa o desactiva a alguien de su equipo. Desactivar corta sus sesiones al instante.</summary>
public sealed record CambiarEstadoUsuario(int ComercioId, int UsuarioId, bool Activo, int? SolicitanteId) : ICommand<Resultado<UsuarioVista>>;

public sealed class CambiarEstadoUsuarioHandler(IAppDbContext db, ServicioSesiones sesiones)
    : ICommandHandler<CambiarEstadoUsuario, Resultado<UsuarioVista>>
{
    public async Task<Resultado<UsuarioVista>> Handle(CambiarEstadoUsuario c, CancellationToken ct)
    {
        var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == c.UsuarioId && x.ComercioId == c.ComercioId, ct);
        if (u is null) return Error.NoEncontrado("Ese usuario no es de tu comercio.");
        if (u.Id == c.SolicitanteId) return Error.Validacion("No podés desactivarte a vos mismo.");

        u.CambiarActivo(c.Activo);
        await db.SaveChangesAsync(ct);
        if (!c.Activo) await sesiones.CerrarTodasDeUsuarioAsync(u.Id, ct: ct);
        return UsuarioVista.Desde(u);
    }
}

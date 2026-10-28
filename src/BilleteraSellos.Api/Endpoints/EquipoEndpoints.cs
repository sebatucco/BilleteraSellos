using System.Security.Claims;
using BilleteraSellos.Aplicacion.Funcionalidades.Equipo;

namespace BilleteraSellos.Api.Endpoints;

public record UsuarioPedido(string? Nombre, string? Email, string? Contrasena);
public record ActivoPedido(bool Activo);

/// <summary>Equipo del comercio: solo el dueño agrega o desactiva cajeros.</summary>
public static class EquipoEndpoints
{
    public static void MapEquipoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/comercios/{comercioId:int}/usuarios").WithTags("Comercio")
            .RequireAuthorization(Politicas.Dueno).AddEndpointFilter<SoloSuComercio>();

        g.MapGet("/", (int comercioId, ListarEquipoHandler h, CancellationToken ct) => h.Handle(new ListarEquipo(comercioId), ct));

        g.MapPost("/", async (int comercioId, UsuarioPedido p, AgregarCajeroHandler h, CancellationToken ct) =>
            (await h.Handle(new AgregarCajero(comercioId, p.Nombre, p.Email, p.Contrasena), ct))
                .ARespuesta(u => Results.Created($"/api/comercios/{comercioId}/usuarios", u)));

        g.MapPut("/{usuarioId:int}/activo", async (int comercioId, int usuarioId, ActivoPedido p, ClaimsPrincipal yo, CambiarEstadoUsuarioHandler h, CancellationToken ct) =>
            (await h.Handle(new CambiarEstadoUsuario(comercioId, usuarioId, p.Activo, yo.Entero(Reclamos.UsuarioId)), ct)).ARespuesta());
    }
}

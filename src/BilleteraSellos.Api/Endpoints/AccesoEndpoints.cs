using System.Security.Claims;
using BilleteraSellos.Aplicacion.Abstracciones;
using BilleteraSellos.Aplicacion.Funcionalidades.Acceso;
using BilleteraSellos.Infraestructura.Servicios;

namespace BilleteraSellos.Api.Endpoints;

public record RegistroComercioPedido(string? NombreComercio, string? Rubro, string? Direccion, string? NombreDueno, string? Email, string? Contrasena);
public record LoginPedido(string? Email, string? Contrasena);
public record CodigoPedido(string? Telefono);
public record CodigoEnviado(string Mensaje, int VenceEnMinutos, string? CodigoDesarrollo);
public record VerificarPedido(string? Telefono, string? Codigo);
public record CambiarContrasenaPedido(string? Actual, string? Nueva);
public record OlvidePedido(string? Email);
public record RestablecerPedido(string? Token, string? Nueva);

/// <summary>Login de comercios y clientes, registro de comercios y contraseñas.</summary>
public static class AccesoEndpoints
{
    public static void MapAccesoEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Login").RequireRateLimiting("auth");

        g.MapPost("/comercios/registro", async (RegistroComercioPedido p, RegistrarComercioHandler h, CancellationToken ct) =>
            (await h.Handle(new RegistrarComercio(p.NombreComercio, p.Rubro, p.Direccion, p.NombreDueno, p.Email, p.Contrasena), ct))
                .ARespuesta(s => Results.Created("/api/auth/yo", s)));

        g.MapPost("/comercios/login", async (LoginPedido p, LoginComercioHandler h, CancellationToken ct) =>
            (await h.Handle(new LoginComercio(p.Email, p.Contrasena), ct)).ARespuesta());

        // Cliente, paso 1: pide un código al teléfono.
        g.MapPost("/clientes/codigo", async (CodigoPedido p, SolicitarCodigoHandler h, IHostEnvironment env, IEnviadorCodigos enviador, CancellationToken ct) =>
        {
            // El código solo vuelve en la respuesta en desarrollo y sin SMS real, para poder probar.
            var mostrarCodigo = env.IsDevelopment() && enviador is EnviadorCodigosConsola;
            return (await h.Handle(new SolicitarCodigo(p.Telefono), ct)).ARespuesta(r =>
                Results.Ok(new CodigoEnviado("Te mandamos un código por SMS.", r.VenceEnMinutos, mostrarCodigo ? r.Codigo : null)));
        });

        // Cliente, paso 2: verifica el código. Entra si ya tenía cuenta; si no, recibe un token para registrarse.
        g.MapPost("/clientes/verificar", async (VerificarPedido p, VerificarCodigoHandler h, CancellationToken ct) =>
            (await h.Handle(new VerificarCodigo(p.Telefono, p.Codigo), ct)).ARespuesta());

        g.MapPost("/salir", async (ClaimsPrincipal u, CerrarSesionHandler h, CancellationToken ct) =>
        {
            if (u.Entero(Reclamos.SesionId) is { } id) await h.Handle(new CerrarSesion(id), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapGet("/yo", async (ClaimsPrincipal u, QuienSoyHandler h, CancellationToken ct) =>
        {
            var tipo = Enum.Parse<TipoSesion>(u.FindFirstValue(Reclamos.Tipo)!);
            var q = new QuienSoy(tipo, u.Entero(Reclamos.UsuarioId), u.Entero(Reclamos.ClienteId), u.FindFirstValue(Reclamos.Telefono));
            return (await h.Handle(q, ct)).ARespuesta();
        }).RequireAuthorization();

        // Cambia la contraseña y cierra las demás sesiones (por si alguien más la sabía).
        g.MapPost("/contrasena", async (CambiarContrasenaPedido p, ClaimsPrincipal u, CambiarContrasenaHandler h, CancellationToken ct) =>
            (await h.Handle(new CambiarContrasena(u.Entero(Reclamos.UsuarioId) ?? 0, u.Entero(Reclamos.SesionId), p.Actual, p.Nueva), ct)).ARespuesta())
            .RequireAuthorization(Politicas.Comercio);

        // Pide un link por email. Responde siempre lo mismo para no revelar qué emails están registrados.
        g.MapPost("/comercios/olvide", async (OlvidePedido p, OlvideContrasenaHandler h, CancellationToken ct) =>
            (await h.Handle(new OlvideContrasena(p.Email), ct)).ARespuesta());

        // Crea la contraseña nueva con el link del email: desbloquea la cuenta y cierra todas las sesiones.
        g.MapPost("/comercios/restablecer", async (RestablecerPedido p, RestablecerContrasenaHandler h, CancellationToken ct) =>
            (await h.Handle(new RestablecerContrasena(p.Token, p.Nueva), ct)).ARespuesta());
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using BilleteraSellos.Aplicacion.Seguridad;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BilleteraSellos.Api.Seguridad;

/// <summary>Nombres de los datos que viajan en la identidad del usuario logueado.</summary>
public static class Reclamos
{
    public const string Tipo = "tipo";
    public const string SesionId = "sesion";
    public const string UsuarioId = "usuario";
    public const string ComercioId = "comercio";
    public const string ClienteId = "cliente";
    public const string Telefono = "telefono";

    public static int? Entero(this ClaimsPrincipal u, string tipo) =>
        int.TryParse(u.FindFirstValue(tipo), out var v) ? v : null;
}

public static class Politicas
{
    public const string Comercio = "comercio";
    public const string Dueno = "dueno";
    public const string Cliente = "cliente";
    public const string Registro = "registro";
}

/// <summary>
/// Lee "Authorization: Bearer &lt;token&gt;" y arma la identidad con la sesión que valida la capa de aplicación.
/// </summary>
public class SesionHandler(IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory log, UrlEncoder enc, ServicioSesiones sesiones)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, log, enc)
{
    public const string Esquema = "Sesion";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var s = await sesiones.ValidarAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (s is null) return AuthenticateResult.Fail("Sesión vencida o cerrada");

        var reclamos = new List<Claim> { new(Reclamos.Tipo, s.Tipo.ToString()), new(Reclamos.SesionId, s.SesionId.ToString()) };
        switch (s.Tipo)
        {
            case TipoSesion.Comercio:
                reclamos.Add(new(Reclamos.UsuarioId, s.UsuarioId!.Value.ToString()));
                reclamos.Add(new(Reclamos.ComercioId, s.ComercioId!.Value.ToString()));
                reclamos.Add(new(ClaimTypes.Name, s.Nombre ?? ""));
                reclamos.Add(new(ClaimTypes.Role, s.Rol!.Value.ToString()));
                break;
            case TipoSesion.Cliente:
                reclamos.Add(new(Reclamos.ClienteId, s.ClienteId!.Value.ToString()));
                break;
            case TipoSesion.Registro:
                reclamos.Add(new(Reclamos.Telefono, s.Telefono ?? ""));
                break;
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(reclamos, Esquema));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Esquema));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Escribir(401, "Tenés que iniciar sesión.");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Escribir(403, "No tenés permiso para hacer esto.");

    Task Escribir(int estado, string error)
    {
        Response.StatusCode = estado;
        return Response.WriteAsJsonAsync(new { error });
    }
}

/// <summary>
/// En todas las rutas /api/comercios/{comercioId}/..., el usuario solo puede operar sobre su propio comercio.
/// </summary>
public class SoloSuComercio : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        if (http.Request.RouteValues.TryGetValue("comercioId", out var valor) &&
            int.TryParse(valor?.ToString(), out var comercioRuta) &&
            http.User.Entero(Reclamos.ComercioId) != comercioRuta)
        {
            return Results.Json(new { error = "No tenés permiso sobre este comercio." }, statusCode: 403);
        }
        return await next(ctx);
    }
}

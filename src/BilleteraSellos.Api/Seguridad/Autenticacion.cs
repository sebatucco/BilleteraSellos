using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BilleteraSellos.Api.Seguridad;

/// <summary>Hash de contraseñas con PBKDF2-SHA256 y sal aleatoria. Formato: v1.iteraciones.sal.hash</summary>
public static class Contrasenas
{
    const int Iteraciones = 210_000;
    const int LargoSal = 16, LargoHash = 32;

    public static string Hashear(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(LargoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, Iteraciones, HashAlgorithmName.SHA256, LargoHash);
        return $"v1.{Iteraciones}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string contrasena, string guardado)
    {
        var partes = guardado.Split('.');
        if (partes.Length != 4 || partes[0] != "v1" || !int.TryParse(partes[1], out var iter)) return false;
        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }

    /// <summary>Devuelve el motivo si la contraseña es débil, o null si sirve.</summary>
    public static string? Validar(string? contrasena)
    {
        if (string.IsNullOrEmpty(contrasena) || contrasena.Length < 8) return "La contraseña tiene que tener al menos 8 caracteres.";
        if (!contrasena.Any(char.IsLetter) || !contrasena.Any(char.IsDigit)) return "La contraseña tiene que tener letras y números.";
        return null;
    }
}

public static class Tokens
{
    /// <summary>Token aleatorio de 256 bits, apto para URL.</summary>
    public static string Generar() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));
}

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

/// <summary>Crea y revoca sesiones.</summary>
public class Sesiones(AppDbContext db)
{
    public static readonly TimeSpan DuracionComercio = TimeSpan.FromDays(7);
    public static readonly TimeSpan DuracionCliente = TimeSpan.FromDays(90);
    public static readonly TimeSpan DuracionRegistro = TimeSpan.FromMinutes(30);

    public async Task<(string Token, DateTime VenceEn)> AbrirAsync(TipoSesion tipo, int? usuarioId = null, int? clienteId = null, string? telefono = null)
    {
        var token = Tokens.Generar();
        var dura = tipo switch { TipoSesion.Comercio => DuracionComercio, TipoSesion.Cliente => DuracionCliente, _ => DuracionRegistro };
        var s = new Sesion
        {
            TokenHash = Tokens.Hash(token), Tipo = tipo, UsuarioId = usuarioId, ClienteId = clienteId,
            Telefono = telefono, VenceEn = DateTime.UtcNow.Add(dura),
        };
        db.Sesiones.Add(s);
        await db.SaveChangesAsync();
        return (token, s.VenceEn);
    }

    public async Task CerrarAsync(int sesionId)
    {
        var s = await db.Sesiones.FirstOrDefaultAsync(x => x.Id == sesionId);
        if (s is null || s.RevocadaEn is not null) return;
        s.RevocadaEn = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Corta todas las sesiones abiertas de un usuario (al desactivarlo o cambiarle la contraseña).</summary>
    public async Task CerrarTodasDeUsuarioAsync(int usuarioId)
    {
        var ahora = DateTime.UtcNow;
        foreach (var s in await db.Sesiones.Where(x => x.UsuarioId == usuarioId && x.RevocadaEn == null).ToListAsync())
            s.RevocadaEn = ahora;
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// Lee "Authorization: Bearer &lt;token&gt;", busca la sesión por el hash del token y arma la identidad.
/// Para comercios verifica en cada pedido que el usuario siga activo, así desactivar un cajero corta su acceso al instante.
/// </summary>
public class SesionHandler(IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory log, UrlEncoder enc, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, log, enc)
{
    public const string Esquema = "Sesion";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        if (token.Length is < 20 or > 100) return AuthenticateResult.Fail("Token inválido");

        var hash = Tokens.Hash(token);
        var s = await db.Sesiones.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (s is null || s.RevocadaEn is not null || s.VenceEn <= DateTime.UtcNow) return AuthenticateResult.Fail("Sesión vencida o cerrada");

        var reclamos = new List<Claim> { new(Reclamos.Tipo, s.Tipo.ToString()), new(Reclamos.SesionId, s.Id.ToString()) };
        switch (s.Tipo)
        {
            case TipoSesion.Comercio:
                var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == s.UsuarioId);
                if (u is null || !u.Activo) return AuthenticateResult.Fail("Usuario desactivado");
                reclamos.Add(new(Reclamos.UsuarioId, u.Id.ToString()));
                reclamos.Add(new(Reclamos.ComercioId, u.ComercioId.ToString()));
                reclamos.Add(new(ClaimTypes.Name, u.Nombre));
                reclamos.Add(new(ClaimTypes.Role, u.Rol.ToString()));
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

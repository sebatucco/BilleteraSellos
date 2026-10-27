using System.Security.Claims;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Endpoints;

public record CambiarContrasenaPedido(string Actual, string Nueva);
public record OlvidePedido(string Email);
public record RestablecerPedido(string Token, string Nueva);

/// <summary>Cambiar la contraseña estando logueado, y recuperarla por email si se olvidó.</summary>
public static class ContrasenaEndpoints
{
    public static readonly TimeSpan VigenciaLink = TimeSpan.FromHours(1);
    public const int MaxLinksPorHora = 3;
    const string RespuestaOlvide = "Si el email está registrado, te mandamos un link para crear una contraseña nueva. Revisá también la carpeta de spam.";

    public static void MapContrasenaEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Login").RequireRateLimiting("auth");

        // Cambia la contraseña y cierra las demás sesiones (por si alguien más la sabía).
        g.MapPost("/contrasena", async (CambiarContrasenaPedido p, ClaimsPrincipal yo, AppDbContext db, Sesiones sesiones) =>
        {
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == yo.Entero(Reclamos.UsuarioId));
            if (u is null) return Results.Unauthorized();
            if (!Contrasenas.Verificar(p.Actual ?? "", u.PasswordHash))
                return Results.BadRequest(new { error = "La contraseña actual no es correcta." });
            if (Contrasenas.Validar(p.Nueva) is { } debil) return Results.BadRequest(new { error = debil });
            if (p.Nueva == p.Actual) return Results.BadRequest(new { error = "La contraseña nueva tiene que ser distinta de la actual." });

            u.PasswordHash = Contrasenas.Hashear(p.Nueva);
            await db.SaveChangesAsync();
            await sesiones.CerrarTodasDeUsuarioAsync(u.Id, salvo: yo.Entero(Reclamos.SesionId));
            return Results.Ok(new { mensaje = "Listo, cambiaste tu contraseña. Cerramos tus sesiones en otros dispositivos." });
        }).RequireAuthorization(Politicas.Comercio);

        // Pide un link por email. Responde siempre lo mismo para no revelar qué emails están registrados.
        g.MapPost("/comercios/olvide", async (OlvidePedido p, AppDbContext db, IEnviadorEmail email, IConfiguration config, ILoggerFactory logs) =>
        {
            var direccion = AuthEndpoints.NormalizarEmail(p.Email);
            if (direccion is null) return Results.BadRequest(new { error = "El email no es válido." });

            var u = await db.Usuarios.Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Email == direccion && x.Activo);
            if (u is null) return Results.Ok(new { mensaje = RespuestaOlvide });

            var ahora = DateTime.UtcNow;
            var hace1h = ahora.AddHours(-1);
            var previos = await db.TokensRestablecimiento.Where(t => t.UsuarioId == u.Id && t.CreadoEn >= hace1h).ToListAsync();
            // Tope silencioso: evita que alguien llene la casilla del dueño pidiendo links.
            if (previos.Count >= MaxLinksPorHora) return Results.Ok(new { mensaje = RespuestaOlvide });

            var token = Tokens.Generar();
            var urlPublica = (config["UrlPublica"] ?? "http://localhost:5225").TrimEnd('/');
            var link = $"{urlPublica}/#restablecer={token}";
            var app = config["NombreApp"] ?? "Billetera de Sellos";
            try
            {
                await email.EnviarAsync(u.Email, $"Crear una contraseña nueva en {app}",
                    $"Hola {u.Nombre}:\n\n" +
                    $"Pediste crear una contraseña nueva para el panel de {u.Comercio!.Nombre}.\n" +
                    $"Entrá a este link (vence en {VigenciaLink.TotalMinutes:0} minutos y sirve una sola vez):\n\n{link}\n\n" +
                    "Si no lo pediste vos, ignorá este email: tu contraseña sigue siendo la misma.\n");
            }
            catch (EnvioFallidoException)
            {
                // El error ya quedó en el log. Al usuario se le responde igual, para no revelar que el email existe.
                return Results.Ok(new { mensaje = RespuestaOlvide });
            }

            // Un link nuevo anula los anteriores sin usar.
            foreach (var t in previos.Where(t => t.UsadoEn == null)) t.UsadoEn = ahora;
            db.TokensRestablecimiento.Add(new TokenRestablecimiento { UsuarioId = u.Id, TokenHash = Tokens.Hash(token), VenceEn = ahora.Add(VigenciaLink) });
            await db.SaveChangesAsync();
            logs.CreateLogger("Contrasenas").LogInformation("Link de restablecimiento enviado al usuario {UsuarioId}", u.Id);
            return Results.Ok(new { mensaje = RespuestaOlvide });
        });

        // Crea la contraseña nueva con el link del email: desbloquea la cuenta y cierra todas las sesiones.
        g.MapPost("/comercios/restablecer", async (RestablecerPedido p, AppDbContext db, Sesiones sesiones) =>
        {
            const string invalido = "El link no es válido o ya venció. Pedí uno nuevo desde \"¿Olvidaste tu contraseña?\".";
            if (string.IsNullOrWhiteSpace(p.Token) || p.Token.Length > 100) return Results.BadRequest(new { error = invalido });
            var hash = Tokens.Hash(p.Token.Trim());
            var t = await db.TokensRestablecimiento.FirstOrDefaultAsync(x => x.TokenHash == hash);
            if (t is null || t.UsadoEn is not null || t.VenceEn <= DateTime.UtcNow) return Results.BadRequest(new { error = invalido });
            if (Contrasenas.Validar(p.Nueva) is { } debil) return Results.BadRequest(new { error = debil });

            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == t.UsuarioId);
            if (u is null || !u.Activo) return Results.BadRequest(new { error = invalido });

            u.PasswordHash = Contrasenas.Hashear(p.Nueva);
            u.IntentosFallidos = 0;
            u.BloqueadoHasta = null;
            t.UsadoEn = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await sesiones.CerrarTodasDeUsuarioAsync(u.Id);
            return Results.Ok(new { mensaje = "Listo, ya podés ingresar con tu contraseña nueva." });
        });
    }
}

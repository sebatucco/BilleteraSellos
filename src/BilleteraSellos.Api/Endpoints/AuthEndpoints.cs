using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Endpoints;

public record RegistroComercioPedido(string NombreComercio, string Rubro, string Direccion, string NombreDueno, string Email, string Contrasena);
public record LoginPedido(string Email, string Contrasena);
public record SesionComercio(string Token, DateTime VenceEn, int UsuarioId, string Nombre, RolUsuario Rol, int ComercioId, string Comercio);
public record CodigoPedido(string Telefono);
public record CodigoEnviado(string Mensaje, int VenceEnMinutos, string? CodigoDesarrollo);
public record VerificarPedido(string Telefono, string Codigo);
/// <summary>Si el teléfono ya tiene cuenta, viene la sesión del cliente; si no, un token para terminar el registro.</summary>
public record ResultadoVerificar(bool RequiereRegistro, string Token, DateTime VenceEn, int? ClienteId, string? Nombre);
public record UsuarioPedido(string Nombre, string Email, string Contrasena);
public record UsuarioVista(int Id, string Nombre, string Email, RolUsuario Rol, bool Activo);
public record ActivoPedido(bool Activo);

public static class AuthEndpoints
{
    public const int MaxIntentosLogin = 5;
    public static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);
    // Hash de una contraseña inexistente: se verifica igual cuando el email no existe, para no revelar qué emails están registrados por el tiempo de respuesta.
    static readonly string HashFicticio = Contrasenas.Hashear(Guid.NewGuid().ToString());

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Login").RequireRateLimiting("auth");

        // Alta de un comercio nuevo con su dueño (prueba gratis, sin intervención).
        g.MapPost("/comercios/registro", async (RegistroComercioPedido p, AppDbContext db, Sesiones sesiones) =>
        {
            if (string.IsNullOrWhiteSpace(p.NombreComercio) || string.IsNullOrWhiteSpace(p.NombreDueno))
                return Results.BadRequest(new { error = "Falta el nombre del comercio o del dueño." });
            var email = NormalizarEmail(p.Email);
            if (email is null) return Results.BadRequest(new { error = "El email no es válido." });
            if (Contrasenas.Validar(p.Contrasena) is { } debil) return Results.BadRequest(new { error = debil });
            if (await db.Usuarios.AnyAsync(u => u.Email == email)) return Results.Conflict(new { error = "Ya hay una cuenta con ese email. Iniciá sesión." });

            var comercio = new Comercio
            {
                Nombre = p.NombreComercio.Trim(), Rubro = p.Rubro?.Trim() ?? "", Direccion = p.Direccion?.Trim() ?? "",
                Codigo = await CodigoUnico(db, p.NombreComercio),
                Programa = new ProgramaSellos { SellosNecesarios = 10, Premio = "Premio a definir" },
            };
            var dueno = new Usuario { Comercio = comercio, Nombre = p.NombreDueno.Trim(), Email = email, PasswordHash = Contrasenas.Hashear(p.Contrasena), Rol = RolUsuario.Dueno };
            db.Comercios.Add(comercio);
            db.Usuarios.Add(dueno);
            await db.SaveChangesAsync();

            var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: dueno.Id);
            return Results.Created("/api/auth/yo", new SesionComercio(token, vence, dueno.Id, dueno.Nombre, dueno.Rol, comercio.Id, comercio.Nombre));
        });

        g.MapPost("/comercios/login", async (LoginPedido p, AppDbContext db, Sesiones sesiones) =>
        {
            const string incorrecto = "Email o contraseña incorrectos.";
            var email = NormalizarEmail(p.Email);
            var u = email is null ? null : await db.Usuarios.Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Email == email);
            if (u is null)
            {
                Contrasenas.Verificar(p.Contrasena ?? "", HashFicticio);
                return Results.Json(new { error = incorrecto }, statusCode: 401);
            }
            if (u.BloqueadoHasta is { } hasta && hasta > DateTime.UtcNow)
                return Results.Json(new { error = $"Demasiados intentos fallidos. Probá de nuevo en {Math.Ceiling((hasta - DateTime.UtcNow).TotalMinutes)} minutos." }, statusCode: 429);

            if (!Contrasenas.Verificar(p.Contrasena ?? "", u.PasswordHash))
            {
                u.IntentosFallidos++;
                if (u.IntentosFallidos >= MaxIntentosLogin) { u.BloqueadoHasta = DateTime.UtcNow.Add(Bloqueo); u.IntentosFallidos = 0; }
                await db.SaveChangesAsync();
                return Results.Json(new { error = incorrecto }, statusCode: 401);
            }
            if (!u.Activo) return Results.Json(new { error = "Tu usuario está desactivado. Hablá con el dueño del comercio." }, statusCode: 403);

            u.IntentosFallidos = 0;
            u.BloqueadoHasta = null;
            await db.SaveChangesAsync();
            var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Comercio, usuarioId: u.Id);
            return Results.Ok(new SesionComercio(token, vence, u.Id, u.Nombre, u.Rol, u.ComercioId, u.Comercio!.Nombre));
        });

        // Cliente, paso 1: pide un código al teléfono.
        g.MapPost("/clientes/codigo", async (CodigoPedido p, CodigosVerificacion codigos, IHostEnvironment env, IEnviadorCodigos enviador) =>
        {
            var tel = CodigosVerificacion.Normalizar(p.Telefono ?? "");
            if (tel.Length is < 8 or > 13) return Results.BadRequest(new { error = "Escribí el teléfono con código de área, por ejemplo 11 5487 3320." });
            ResultadoSolicitud resultado; string? codigo;
            try { (resultado, codigo) = await codigos.SolicitarAsync(tel); }
            catch (EnvioFallidoException ex) { return Results.Json(new { error = ex.Message }, statusCode: 502); }
            // Solo se devuelve el código si es desarrollo y no hay SMS real configurado.
            if (!env.IsDevelopment() || enviador is not EnviadorCodigosConsola) codigo = null;
            return resultado switch
            {
                ResultadoSolicitud.EsperarUnMinuto => Results.Json(new { error = "Ya te mandamos un código. Esperá un minuto para pedir otro." }, statusCode: 429),
                ResultadoSolicitud.DemasiadosPorHora => Results.Json(new { error = "Pediste demasiados códigos. Probá de nuevo en una hora." }, statusCode: 429),
                // El código solo vuelve en la respuesta en desarrollo, para poder probar sin SMS.
                _ => Results.Ok(new CodigoEnviado("Te mandamos un código por SMS.", (int)CodigosVerificacion.Vigencia.TotalMinutes, codigo)),
            };
        });

        // Cliente, paso 2: verifica el código. Entra si ya tenía cuenta; si no, recibe un token para registrarse.
        g.MapPost("/clientes/verificar", async (VerificarPedido p, CodigosVerificacion codigos, AppDbContext db, Sesiones sesiones) =>
        {
            var tel = CodigosVerificacion.Normalizar(p.Telefono ?? "");
            var r = await codigos.VerificarAsync(tel, p.Codigo ?? "");
            switch (r)
            {
                case ResultadoVerificacion.Incorrecto: return Results.Json(new { error = "El código no es correcto." }, statusCode: 401);
                case ResultadoVerificacion.VencidoOInexistente: return Results.Json(new { error = "El código venció o no existe. Pedí uno nuevo." }, statusCode: 401);
                case ResultadoVerificacion.DemasiadosIntentos: return Results.Json(new { error = "Demasiados intentos. Pedí un código nuevo." }, statusCode: 429);
            }

            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefono == tel);
            if (cliente is null)
            {
                var (tr, vr) = await sesiones.AbrirAsync(TipoSesion.Registro, telefono: tel);
                return Results.Ok(new ResultadoVerificar(true, tr, vr, null, null));
            }
            var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: cliente.Id);
            return Results.Ok(new ResultadoVerificar(false, token, vence, cliente.Id, cliente.Nombre));
        });

        g.MapPost("/salir", async (ClaimsPrincipal u, Sesiones sesiones) =>
        {
            if (u.Entero(Reclamos.SesionId) is { } id) await sesiones.CerrarAsync(id);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapGet("/yo", async (ClaimsPrincipal u, AppDbContext db) =>
        {
            var tipo = u.FindFirstValue(Reclamos.Tipo);
            if (tipo == nameof(TipoSesion.Comercio))
            {
                var usr = await db.Usuarios.Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Id == u.Entero(Reclamos.UsuarioId));
                return Results.Ok(new { tipo, usuarioId = usr!.Id, usr.Nombre, rol = usr.Rol.ToString(), usr.ComercioId, comercio = usr.Comercio!.Nombre });
            }
            if (tipo == nameof(TipoSesion.Cliente))
            {
                var c = await db.Clientes.FirstOrDefaultAsync(x => x.Id == u.Entero(Reclamos.ClienteId));
                return c is null ? Results.Unauthorized() : Results.Ok(new { tipo, clienteId = c.Id, c.Nombre });
            }
            return Results.Ok(new { tipo, telefono = u.FindFirstValue(Reclamos.Telefono) });
        }).RequireAuthorization();

        // Equipo del comercio: solo el dueño agrega o desactiva cajeros.
        var equipo = app.MapGroup("/api/comercios/{comercioId:int}/usuarios").WithTags("Comercio")
            .RequireAuthorization(Politicas.Dueno).AddEndpointFilter<SoloSuComercio>();

        equipo.MapGet("/", async (int comercioId, AppDbContext db) =>
            await db.Usuarios.Where(u => u.ComercioId == comercioId).OrderBy(u => u.Rol).ThenBy(u => u.Nombre)
                .Select(u => new UsuarioVista(u.Id, u.Nombre, u.Email, u.Rol, u.Activo)).ToListAsync());

        equipo.MapPost("/", async (int comercioId, UsuarioPedido p, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(p.Nombre)) return Results.BadRequest(new { error = "Falta el nombre." });
            var email = NormalizarEmail(p.Email);
            if (email is null) return Results.BadRequest(new { error = "El email no es válido." });
            if (Contrasenas.Validar(p.Contrasena) is { } debil) return Results.BadRequest(new { error = debil });
            if (await db.Usuarios.AnyAsync(u => u.Email == email)) return Results.Conflict(new { error = "Ya hay un usuario con ese email." });

            var u = new Usuario { ComercioId = comercioId, Nombre = p.Nombre.Trim(), Email = email, PasswordHash = Contrasenas.Hashear(p.Contrasena), Rol = RolUsuario.Cajero };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            return Results.Created($"/api/comercios/{comercioId}/usuarios", new UsuarioVista(u.Id, u.Nombre, u.Email, u.Rol, u.Activo));
        });

        equipo.MapPut("/{usuarioId:int}/activo", async (int comercioId, int usuarioId, ActivoPedido p, ClaimsPrincipal yo, AppDbContext db, Sesiones sesiones) =>
        {
            var u = await db.Usuarios.FirstOrDefaultAsync(x => x.Id == usuarioId && x.ComercioId == comercioId);
            if (u is null) return Results.NotFound(new { error = "Ese usuario no es de tu comercio." });
            if (u.Id == yo.Entero(Reclamos.UsuarioId)) return Results.BadRequest(new { error = "No podés desactivarte a vos mismo." });
            u.Activo = p.Activo;
            await db.SaveChangesAsync();
            if (!p.Activo) await sesiones.CerrarTodasDeUsuarioAsync(u.Id);
            return Results.Ok(new UsuarioVista(u.Id, u.Nombre, u.Email, u.Rol, u.Activo));
        });
    }

    internal static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200) return null;
        try { return new MailAddress(email.Trim()).Address.ToLowerInvariant(); }
        catch (FormatException) { return null; }
    }

    /// <summary>Arma el código del QR del mostrador a partir del nombre: "Café Tostado" → "cafe-tostado".</summary>
    static async Task<string> CodigoUnico(AppDbContext db, string nombre)
    {
        var sb = new StringBuilder();
        foreach (var ch in nombre.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        var baseCodigo = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (baseCodigo.Length == 0) baseCodigo = "comercio";
        if (baseCodigo.Length > 30) baseCodigo = baseCodigo[..30].TrimEnd('-');

        var codigo = baseCodigo;
        for (var n = 2; await db.Comercios.AnyAsync(c => c.Codigo == codigo); n++) codigo = $"{baseCodigo}-{n}";
        return codigo;
    }
}

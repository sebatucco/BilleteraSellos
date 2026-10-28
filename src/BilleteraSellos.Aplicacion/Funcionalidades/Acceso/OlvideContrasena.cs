using Microsoft.Extensions.Logging;

namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Pide un link por email para crear una contraseña nueva. Responde siempre lo mismo, exista o no el email.</summary>
public sealed record OlvideContrasena(string? Email) : ICommand<Resultado<RespuestaMensaje>>;

public sealed class OlvideContrasenaHandler(
    IAppDbContext db, IEnviadorEmail email, OpcionesApp opciones, IReloj reloj, ILogger<OlvideContrasenaHandler> log)
    : ICommandHandler<OlvideContrasena, Resultado<RespuestaMensaje>>
{
    public const string Respuesta = "Si el email está registrado, te mandamos un link para crear una contraseña nueva. Revisá también la carpeta de spam.";

    public async Task<Resultado<RespuestaMensaje>> Handle(OlvideContrasena c, CancellationToken ct)
    {
        var direccion = Emails.Normalizar(c.Email);
        if (direccion is null) return Error.Validacion("El email no es válido.");
        var ok = new RespuestaMensaje(Respuesta);

        var u = await db.Usuarios.Include(x => x.Comercio).FirstOrDefaultAsync(x => x.Email == direccion && x.Activo, ct);
        if (u is null) return ok;

        var ahora = reloj.UtcAhora;
        var hace1h = ahora.AddHours(-1);
        var previos = await db.TokensRestablecimiento.Where(t => t.UsuarioId == u.Id && t.CreadoEn >= hace1h).ToListAsync(ct);
        // Tope silencioso: evita que alguien llene la casilla del dueño pidiendo links.
        if (previos.Count >= TokenRestablecimiento.MaxPorHora) return ok;

        var token = Tokens.Generar();
        var link = $"{opciones.UrlPublica.TrimEnd('/')}/#restablecer={token}";
        try
        {
            await email.EnviarAsync(u.Email, $"Crear una contraseña nueva en {opciones.NombreApp}",
                $"Hola {u.Nombre}:\n\n" +
                $"Pediste crear una contraseña nueva para el panel de {u.Comercio!.Nombre}.\n" +
                $"Entrá a este link (vence en {TokenRestablecimiento.Vigencia.TotalMinutes:0} minutos y sirve una sola vez):\n\n{link}\n\n" +
                "Si no lo pediste vos, ignorá este email: tu contraseña sigue siendo la misma.\n", ct);
        }
        catch (EnvioFallidoException)
        {
            // El error ya quedó en el log. Al usuario se le responde igual, para no revelar que el email existe.
            return ok;
        }

        // Un link nuevo anula los anteriores sin usar.
        foreach (var t in previos) t.Usar(ahora);
        db.TokensRestablecimiento.Add(TokenRestablecimiento.Crear(u.Id, Tokens.Hash(token), ahora));
        await db.SaveChangesAsync(ct);
        log.LogInformation("Link de restablecimiento enviado al usuario {UsuarioId}", u.Id);
        return ok;
    }
}

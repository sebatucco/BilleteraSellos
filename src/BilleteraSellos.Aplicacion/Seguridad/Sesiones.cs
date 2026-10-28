using System.Security.Cryptography;
using System.Text;

namespace BilleteraSellos.Aplicacion.Seguridad;

public static class Tokens
{
    /// <summary>Token aleatorio de 256 bits, apto para URL.</summary>
    public static string Generar() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    /// <summary>Compara dos hashes hexadecimales en tiempo constante.</summary>
    public static bool HashesIguales(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));
}

/// <summary>Lo que se sabe de quien hizo el pedido, a partir de su token.</summary>
public sealed record SesionValida(
    int SesionId, TipoSesion Tipo,
    int? UsuarioId, int? ComercioId, string? Nombre, RolUsuario? Rol,
    int? ClienteId, string? Telefono);

/// <summary>Abre, valida y cierra sesiones. El token solo existe del lado del usuario; acá se guarda su hash.</summary>
public class ServicioSesiones(IAppDbContext db, IReloj reloj)
{
    public async Task<(string Token, DateTime VenceEn)> AbrirAsync(TipoSesion tipo, int? usuarioId = null, int? clienteId = null, string? telefono = null, CancellationToken ct = default)
    {
        var token = Tokens.Generar();
        var s = Sesion.Abrir(tipo, Tokens.Hash(token), reloj.UtcAhora, usuarioId, clienteId, telefono);
        db.Sesiones.Add(s);
        await db.SaveChangesAsync(ct);
        return (token, s.VenceEn);
    }

    public async Task CerrarAsync(int sesionId, CancellationToken ct = default)
    {
        var s = await db.Sesiones.FirstOrDefaultAsync(x => x.Id == sesionId, ct);
        if (s is null || s.RevocadaEn is not null) return;
        s.Revocar(reloj.UtcAhora);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Corta todas las sesiones abiertas de un usuario (al desactivarlo o cambiarle la contraseña).
    /// Con <paramref name="salvo"/> deja abierta esa sesión (la de quien está haciendo el cambio).</summary>
    public async Task CerrarTodasDeUsuarioAsync(int usuarioId, int? salvo = null, CancellationToken ct = default)
    {
        var ahora = reloj.UtcAhora;
        var abiertas = await db.Sesiones.Where(x => x.UsuarioId == usuarioId && x.RevocadaEn == null && x.Id != salvo).ToListAsync(ct);
        foreach (var s in abiertas) s.Revocar(ahora);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Busca la sesión por el hash del token. Para comercios verifica que el usuario siga activo,
    /// así desactivar un cajero corta su acceso al instante. Devuelve null si no sirve.
    /// </summary>
    public async Task<SesionValida?> ValidarAsync(string token, CancellationToken ct = default)
    {
        if (token.Length is < 20 or > 100) return null;
        var hash = Tokens.Hash(token);
        var s = await db.Sesiones.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (s is null || !s.EstaVigente(reloj.UtcAhora)) return null;

        switch (s.Tipo)
        {
            case TipoSesion.Comercio:
                var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.UsuarioId, ct);
                if (u is null || !u.Activo) return null;
                return new SesionValida(s.Id, s.Tipo, u.Id, u.ComercioId, u.Nombre, u.Rol, null, null);
            case TipoSesion.Cliente:
                return s.ClienteId is { } clienteId ? new SesionValida(s.Id, s.Tipo, null, null, null, null, clienteId, null) : null;
            default:
                return new SesionValida(s.Id, s.Tipo, null, null, null, null, null, s.Telefono ?? "");
        }
    }
}

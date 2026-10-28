using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BilleteraSellos.Infraestructura.Servicios;

/// <summary>Hora real. La fecha local usa la zona horaria configurada (Argentina por defecto) para los cumpleaños.</summary>
public class RelojSistema(IConfiguration config) : IReloj
{
    readonly TimeZoneInfo _zona = TimeZoneInfo.FindSystemTimeZoneById(config["ZonaHoraria"] ?? "America/Argentina/Buenos_Aires");

    public DateTime UtcAhora => DateTime.UtcNow;
    public DateTime AhoraLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zona);
    public DateOnly HoyLocal => DateOnly.FromDateTime(AhoraLocal);
}

/// <summary>Hash de contraseñas con PBKDF2-SHA256 y sal aleatoria. Formato: v1.iteraciones.sal.hash</summary>
public class HasherPbkdf2 : IHasherContrasenas
{
    const int Iteraciones = 210_000;
    const int LargoSal = 16, LargoHash = 32;

    public string Hashear(string contrasena)
    {
        var sal = RandomNumberGenerator.GetBytes(LargoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, Iteraciones, HashAlgorithmName.SHA256, LargoHash);
        return $"v1.{Iteraciones}.{Convert.ToBase64String(sal)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string contrasena, string hashGuardado)
    {
        var partes = hashGuardado.Split('.');
        if (partes.Length != 4 || partes[0] != "v1" || !int.TryParse(partes[1], out var iter)) return false;
        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }
}

/// <summary>
/// Avisos al cliente. Hoy solo los escribe en el log; después se cambia por notificaciones de
/// Apple/Google Wallet, push (Firebase) o WhatsApp sin tocar el resto del código.
/// </summary>
public class NotificadorConsola(ILogger<NotificadorConsola> log) : INotificador
{
    public Task EnviarAsync(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct = default)
    {
        log.LogInformation("[{Tipo}] {Comercio} → {Cliente} ({Telefono}): {Mensaje}", tipo, comercio.Nombre, cliente.Nombre, cliente.Telefono, mensaje);
        return Task.CompletedTask;
    }
}

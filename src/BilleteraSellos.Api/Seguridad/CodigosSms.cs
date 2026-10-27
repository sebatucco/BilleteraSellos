using System.Security.Cryptography;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Seguridad;

/// <summary>
/// Canal por el que sale el código de verificación. Hoy lo escribe en la consola;
/// en producción se reemplaza por SMS o WhatsApp (por ejemplo Twilio) sin tocar el resto.
/// </summary>
public interface IEnviadorCodigos
{
    Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default);
}

public class EnviadorCodigosConsola(ILogger<EnviadorCodigosConsola> log) : IEnviadorCodigos
{
    public Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default)
    {
        log.LogWarning("CÓDIGO DE VERIFICACIÓN para {Telefono}: {Codigo} (solo desarrollo: en producción va por SMS)", telefono, codigo);
        return Task.CompletedTask;
    }
}

public enum ResultadoSolicitud { Enviado, EsperarUnMinuto, DemasiadosPorHora }
public enum ResultadoVerificacion { Correcto, Incorrecto, VencidoOInexistente, DemasiadosIntentos }

public class CodigosVerificacion(AppDbContext db, IEnviadorCodigos enviador)
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);
    public const int MaxIntentos = 5;
    public const int MaxPorHora = 5;

    /// <summary>Teléfono argentino normalizado: sin +54 ni el 9 de celulares, solo dígitos.</summary>
    public static string Normalizar(string telefono)
    {
        var d = Codigos.SoloDigitos(telefono);
        if (d.StartsWith("549")) d = d[3..];
        else if (d.StartsWith("54")) d = d[2..];
        return d;
    }

    public async Task<(ResultadoSolicitud Resultado, string? Codigo)> SolicitarAsync(string telefono, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var hace1h = ahora.AddHours(-1);
        var recientes = await db.CodigosVerificacion.Where(c => c.Telefono == telefono && c.CreadoEn >= hace1h).ToListAsync(ct);
        if (recientes.Any(c => c.CreadoEn >= ahora.AddMinutes(-1))) return (ResultadoSolicitud.EsperarUnMinuto, null);
        if (recientes.Count >= MaxPorHora) return (ResultadoSolicitud.DemasiadosPorHora, null);

        // Un código nuevo anula los anteriores sin usar.
        foreach (var c in recientes.Where(c => c.UsadoEn == null)) c.UsadoEn = ahora;

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.CodigosVerificacion.Add(new CodigoVerificacion { Telefono = telefono, CodigoHash = Tokens.Hash(telefono + ":" + codigo), VenceEn = ahora.Add(Vigencia) });
        await db.SaveChangesAsync(ct);
        await enviador.EnviarAsync(telefono, codigo, ct);
        return (ResultadoSolicitud.Enviado, codigo);
    }

    public async Task<ResultadoVerificacion> VerificarAsync(string telefono, string codigo, CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var vigentes = await db.CodigosVerificacion
            .Where(c => c.Telefono == telefono && c.UsadoEn == null && c.VenceEn > ahora).ToListAsync(ct);
        var actual = vigentes.OrderByDescending(c => c.CreadoEn).FirstOrDefault();
        if (actual is null) return ResultadoVerificacion.VencidoOInexistente;
        if (actual.Intentos >= MaxIntentos) return ResultadoVerificacion.DemasiadosIntentos;

        actual.Intentos++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual.CodigoHash),
            Convert.FromHexString(Tokens.Hash(telefono + ":" + Codigos.SoloDigitos(codigo))));
        if (ok) actual.UsadoEn = ahora;
        await db.SaveChangesAsync(ct);
        return ok ? ResultadoVerificacion.Correcto
             : actual.Intentos >= MaxIntentos ? ResultadoVerificacion.DemasiadosIntentos
             : ResultadoVerificacion.Incorrecto;
    }
}

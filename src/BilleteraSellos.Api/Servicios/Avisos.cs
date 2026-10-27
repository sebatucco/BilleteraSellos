using BilleteraSellos.Api.Dominio;

namespace BilleteraSellos.Api.Servicios;

/// <summary>
/// Canal por el que le llega un aviso al cliente. Hoy solo lo escribe en el log;
/// después se cambia por notificaciones de Apple/Google Wallet, push (Firebase) o WhatsApp
/// sin tocar el resto del código.
/// </summary>
public interface INotificador
{
    Task EnviarAsync(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct = default);
}

public class NotificadorConsola(ILogger<NotificadorConsola> log) : INotificador
{
    public Task EnviarAsync(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct = default)
    {
        log.LogInformation("[{Tipo}] {Comercio} → {Cliente} ({Telefono}): {Mensaje}", tipo, comercio.Nombre, cliente.Nombre, cliente.Telefono, mensaje);
        return Task.CompletedTask;
    }
}

/// <summary>Hora local del negocio, para que los cumpleaños se calculen con el día de Argentina y no con UTC.</summary>
public class RelojLocal(IConfiguration config)
{
    readonly TimeZoneInfo _zona = TimeZoneInfo.FindSystemTimeZoneById(config["ZonaHoraria"] ?? "America/Argentina/Buenos_Aires");
    public DateTime Ahora => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zona);
    public DateOnly Hoy => DateOnly.FromDateTime(Ahora);
}

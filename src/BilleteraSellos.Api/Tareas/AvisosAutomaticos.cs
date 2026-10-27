using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Tareas;

public record ResultadoAvisos(int Cumpleanos, int Reactivaciones);

/// <summary>Decide a quién saludar por su cumpleaños y a quién invitar a volver. No repite avisos.</summary>
public class ProcesadorAvisos(AppDbContext db, INotificador notificador, RelojLocal reloj)
{
    /// <summary>Nunca se le escribe al mismo cliente por reactivación más de una vez en este plazo.</summary>
    public const int DiasEntreReactivaciones = 14;

    public async Task<ResultadoAvisos> ProcesarAsync(CancellationToken ct = default)
    {
        var hoy = reloj.Hoy;
        var ahoraUtc = DateTime.UtcNow;

        var tarjetas = await db.Tarjetas
            .Include(t => t.Cliente)
            .Include(t => t.Comercio).ThenInclude(c => c!.Programa)
            .Include(t => t.Movimientos)
            .Where(t => t.AceptaPromos)
            .ToListAsync(ct);

        var desde = ahoraUtc.AddDays(-DiasEntreReactivaciones);
        var desdeAnio = ahoraUtc.AddDays(-300);
        var recientes = await db.Avisos.Where(a => a.EnviadoEn >= desdeAnio).ToListAsync(ct);

        int cumple = 0, reactiva = 0;
        foreach (var t in tarjetas)
        {
            var cliente = t.Cliente!;
            var comercio = t.Comercio!;

            if (EsSuCumpleanos(cliente.FechaNacimiento, hoy) &&
                !recientes.Any(a => a.ClienteId == cliente.Id && a.ComercioId == comercio.Id && a.Tipo == TipoAviso.Cumpleanos))
            {
                var nombre = cliente.Nombre.Split(' ')[0];
                await Registrar(cliente, comercio, TipoAviso.Cumpleanos, $"¡Feliz cumpleaños, {nombre}! Te saludamos desde {comercio.Nombre}.", ct);
                cumple++;
                continue; // el saludo de cumpleaños ya cumple la función de recordatorio
            }

            var visitas = t.Movimientos.Where(m => m.Tipo == TipoMovimiento.Sello).Select(m => m.Fecha).ToList();
            var ritmo = Enfriamiento.Evaluar(visitas, ahoraUtc);
            // Cualquier aviso de este comercio en los últimos días (incluido el de cumpleaños) frena la reactivación.
            if (ritmo.Estado is EstadoCliente.EnRiesgo or EstadoCliente.Critico &&
                !recientes.Any(a => a.ClienteId == cliente.Id && a.ComercioId == comercio.Id && a.EnviadoEn >= desde))
            {
                var faltan = comercio.Programa is { } p ? p.SellosNecesarios - t.SellosActuales : 0;
                var mensaje = faltan > 0
                    ? $"Te extrañamos en {comercio.Nombre}. {(faltan == 1 ? "Te falta 1 sello" : $"Te faltan {faltan} sellos")} para tu premio."
                    : $"Te extrañamos en {comercio.Nombre}. Tenés un premio esperándote.";
                await Registrar(cliente, comercio, TipoAviso.Reactivacion, mensaje, ct);
                reactiva++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ResultadoAvisos(cumple, reactiva);
    }

    async Task Registrar(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct)
    {
        await notificador.EnviarAsync(cliente, comercio, tipo, mensaje, ct);
        db.Avisos.Add(new Aviso { ClienteId = cliente.Id, ComercioId = comercio.Id, Tipo = tipo, Mensaje = mensaje });
    }

    public static bool EsSuCumpleanos(DateOnly? nacimiento, DateOnly hoy)
    {
        if (nacimiento is not { } n) return false;
        // Los nacidos el 29/2 festejan el 28/2 en años no bisiestos.
        if (n.Month == 2 && n.Day == 29 && !DateTime.IsLeapYear(hoy.Year)) return hoy.Month == 2 && hoy.Day == 28;
        return n.Month == hoy.Month && n.Day == hoy.Day;
    }
}

/// <summary>Corre en segundo plano y procesa los avisos una vez por día a la hora configurada.</summary>
public class AvisosAutomaticos(IServiceScopeFactory scopes, RelojLocal reloj, IConfiguration config, ILogger<AvisosAutomaticos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var horaEnvio = config.GetValue("Avisos:HoraDeEnvio", 10);
        DateOnly? ultimoDia = null;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            var ahora = reloj.Ahora;
            if (ahora.Hour >= horaEnvio && ultimoDia != reloj.Hoy)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var r = await scope.ServiceProvider.GetRequiredService<ProcesadorAvisos>().ProcesarAsync(ct);
                    log.LogInformation("Avisos del día: {Cumple} cumpleaños, {React} reactivaciones", r.Cumpleanos, r.Reactivaciones);
                    ultimoDia = reloj.Hoy;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Falló el envío de avisos; se reintenta en 5 minutos");
                }
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}

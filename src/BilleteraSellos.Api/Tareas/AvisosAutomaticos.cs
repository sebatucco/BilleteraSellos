using BilleteraSellos.Aplicacion.Abstracciones;
using BilleteraSellos.Aplicacion.Funcionalidades.Avisos;

namespace BilleteraSellos.Api.Tareas;

/// <summary>Corre en segundo plano y procesa los avisos (cumpleaños y reactivación) una vez por día a la hora configurada.</summary>
public class AvisosAutomaticos(IServiceScopeFactory scopes, IReloj reloj, IConfiguration config, ILogger<AvisosAutomaticos> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var horaEnvio = config.GetValue("Avisos:HoraDeEnvio", 10);
        DateOnly? ultimoDia = null;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            if (reloj.AhoraLocal.Hour >= horaEnvio && ultimoDia != reloj.HoyLocal)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var r = await scope.ServiceProvider.GetRequiredService<ProcesarAvisosHandler>().Handle(new ProcesarAvisos(), ct);
                    log.LogInformation("Avisos del día: {Cumple} cumpleaños, {React} reactivaciones", r.Cumpleanos, r.Reactivaciones);
                    ultimoDia = reloj.HoyLocal;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Falló el envío de avisos; se reintenta en 5 minutos");
                }
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}

namespace BilleteraSellos.Aplicacion.Funcionalidades.Avisos;

/// <summary>
/// Decide a quién saludar por su cumpleaños y a quién invitar a volver (según su propio ritmo de visitas).
/// Respeta el consentimiento de cada tarjeta y no repite avisos.
/// </summary>
public sealed record ProcesarAvisos : ICommand<ResultadoAvisos>;

public sealed record ResultadoAvisos(int Cumpleanos, int Reactivaciones);

public sealed class ProcesarAvisosHandler(IAppDbContext db, INotificador notificador, IReloj reloj) : ICommandHandler<ProcesarAvisos, ResultadoAvisos>
{
    /// <summary>Nunca se le escribe al mismo cliente, desde el mismo comercio, más de una vez en este plazo para invitarlo a volver.</summary>
    public const int DiasEntreReactivaciones = 14;

    public async Task<ResultadoAvisos> Handle(ProcesarAvisos c, CancellationToken ct)
    {
        var hoy = reloj.HoyLocal;
        var ahora = reloj.UtcAhora;

        var tarjetas = await db.Tarjetas
            .Include(t => t.Cliente)
            .Include(t => t.Comercio).ThenInclude(co => co!.Programa)
            .Include(t => t.Movimientos)
            .Where(t => t.AceptaPromos)
            .ToListAsync(ct);

        var desde = ahora.AddDays(-DiasEntreReactivaciones);
        var desdeAnio = ahora.AddDays(-300);
        var recientes = await db.Avisos.AsNoTracking().Where(a => a.EnviadoEn >= desdeAnio).ToListAsync(ct);

        int cumple = 0, reactiva = 0;
        foreach (var t in tarjetas)
        {
            var cliente = t.Cliente!;
            var comercio = t.Comercio!;

            if (Cumpleanos.EsSuCumpleanos(cliente.FechaNacimiento, hoy) &&
                !recientes.Any(a => a.ClienteId == cliente.Id && a.ComercioId == comercio.Id && a.Tipo == TipoAviso.Cumpleanos))
            {
                var nombre = cliente.Nombre.Split(' ')[0];
                await Registrar(cliente, comercio, TipoAviso.Cumpleanos, $"¡Feliz cumpleaños, {nombre}! Te saludamos desde {comercio.Nombre}.", ahora, ct);
                cumple++;
                continue; // el saludo de cumpleaños ya cumple la función de recordatorio
            }

            var ritmo = Enfriamiento.Evaluar(t.Visitas(), ahora);
            // Cualquier aviso de este comercio en los últimos días (incluido el de cumpleaños) frena la reactivación.
            if (Enfriamiento.EstaEnfriandose(ritmo) &&
                !recientes.Any(a => a.ClienteId == cliente.Id && a.ComercioId == comercio.Id && a.EnviadoEn >= desde))
            {
                var faltan = comercio.Programa is { } p ? p.SellosNecesarios - t.SellosActuales : 0;
                var mensaje = faltan > 0
                    ? $"Te extrañamos en {comercio.Nombre}. {(faltan == 1 ? "Te falta 1 sello" : $"Te faltan {faltan} sellos")} para tu premio."
                    : $"Te extrañamos en {comercio.Nombre}. Tenés un premio esperándote.";
                await Registrar(cliente, comercio, TipoAviso.Reactivacion, mensaje, ahora, ct);
                reactiva++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new ResultadoAvisos(cumple, reactiva);
    }

    async Task Registrar(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, DateTime ahora, CancellationToken ct)
    {
        await notificador.EnviarAsync(cliente, comercio, tipo, mensaje, ct);
        db.Avisos.Add(Aviso.Crear(cliente.Id, comercio.Id, tipo, mensaje, ahora));
    }
}

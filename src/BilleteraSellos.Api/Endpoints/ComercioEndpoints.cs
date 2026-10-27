using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Endpoints;

public record ComercioResumido(int Id, string Nombre, string Rubro, string Direccion, string Codigo, string Color, string? Premio, int? SellosNecesarios);
public record Resumen(int Clientes, int SellosUltimos30Dias, int ClientesEnRiesgo, int CanjesPendientes);
public record ClienteEnCaja(int ClienteId, string Nombre, bool TieneTarjeta, int SellosActuales, int SellosNecesarios, Nivel Nivel, bool PremioDisponible);
public record SumarSelloPedido(int ClienteId, string? CargadoPor);
public record SelloSumado(int TarjetaId, int SellosActuales, int SellosNecesarios, bool TarjetaCompleta, Nivel Nivel, bool TarjetaNueva);
public record ClienteEnfriandose(int ClienteId, string Nombre, string Telefono, int DiasSinVenir, double? CadaCuantosDias, EstadoCliente Estado, bool AceptaPromos);
public record Cumpleanero(int ClienteId, string Nombre, DateOnly Fecha, int EnDias);
public record ProgramaPedido(int SellosNecesarios, string Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos);
public record CanjeEntregado(string Codigo, string Premio, string Cliente);

public static class ComercioEndpoints
{
    public static void MapComercioEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/comercios").WithTags("Comercio");

        // Directorio de comercios adheridos (lo usa la billetera para "cerca tuyo").
        g.MapGet("/", async (AppDbContext db) =>
            await db.Comercios.Include(c => c.Programa).OrderBy(c => c.Nombre)
                .Select(c => new ComercioResumido(c.Id, c.Nombre, c.Rubro, c.Direccion, c.Codigo, c.Color,
                    c.Programa != null ? c.Programa.Premio : null, c.Programa != null ? c.Programa.SellosNecesarios : null))
                .ToListAsync());

        g.MapGet("/{comercioId:int}/resumen", async (int comercioId, AppDbContext db) =>
        {
            if (!await db.Comercios.AnyAsync(c => c.Id == comercioId)) return Results.NotFound();
            var desde = DateTime.UtcNow.AddDays(-30);
            var tarjetas = await db.Tarjetas.Include(t => t.Movimientos).Include(t => t.Canjes)
                .Where(t => t.ComercioId == comercioId).ToListAsync();
            var ahora = DateTime.UtcNow;
            return Results.Ok(new Resumen(
                tarjetas.Count,
                tarjetas.Sum(t => t.Movimientos.Count(m => m.Tipo == TipoMovimiento.Sello && m.Fecha >= desde)),
                tarjetas.Count(t => Enfriamiento.Evaluar(Visitas(t), ahora).Estado is EstadoCliente.EnRiesgo or EstadoCliente.Critico),
                tarjetas.Sum(t => t.Canjes.Count(c => c.EntregadoEn == null && c.VenceEn > ahora))));
        });

        // Buscar en la caja por DNI, teléfono o el código del QR de la billetera.
        g.MapGet("/{comercioId:int}/clientes/buscar", async (int comercioId, string q, AppDbContext db) =>
        {
            var programa = await db.Programas.FirstOrDefaultAsync(p => p.ComercioId == comercioId);
            if (programa is null) return Results.NotFound(new { error = "El comercio no existe o no tiene tarjeta configurada." });

            var cliente = await BuscarCliente(db, q);
            if (cliente is null) return Results.NotFound(new { error = "No hay ningún cliente con ese DNI, teléfono o QR. Registralo como cliente nuevo." });

            var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.ClienteId == cliente.Id && x.ComercioId == comercioId);
            return Results.Ok(new ClienteEnCaja(cliente.Id, cliente.Nombre, t is not null, t?.SellosActuales ?? 0,
                programa.SellosNecesarios, Niveles.Calcular(t?.SellosTotales ?? 0), t is not null && t.SellosActuales >= programa.SellosNecesarios));
        });

        g.MapPost("/{comercioId:int}/sellos", async (int comercioId, SumarSelloPedido pedido, AppDbContext db, INotificador notificador) =>
        {
            var comercio = await db.Comercios.Include(c => c.Programa).FirstOrDefaultAsync(c => c.Id == comercioId);
            if (comercio?.Programa is not { } programa) return Results.NotFound(new { error = "El comercio no existe o no tiene tarjeta configurada." });
            var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Id == pedido.ClienteId);
            if (cliente is null) return Results.NotFound(new { error = "El cliente no existe." });

            var tarjeta = await db.Tarjetas.FirstOrDefaultAsync(t => t.ClienteId == cliente.Id && t.ComercioId == comercioId);
            var nueva = tarjeta is null;
            if (tarjeta is null)
            {
                tarjeta = new Tarjeta { ClienteId = cliente.Id, ComercioId = comercioId };
                db.Tarjetas.Add(tarjeta);
            }

            if (tarjeta.SellosActuales >= programa.SellosNecesarios)
                return Results.Conflict(new { error = "La tarjeta ya está completa. El cliente tiene que canjear el premio antes de sumar más sellos." });
            if (tarjeta.UltimoSello is { } ultimo && DateTime.UtcNow - ultimo < TimeSpan.FromHours(programa.HorasEntreSellos))
                return Results.Conflict(new { error = $"Ya se sumó un sello hace menos de {programa.HorasEntreSellos} h. Si es una compra nueva, esperá o cambiá la regla del programa." });

            tarjeta.SellosActuales++;
            tarjeta.SellosTotales++;
            tarjeta.UltimoSello = DateTime.UtcNow;
            tarjeta.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Sello, CargadoPor = pedido.CargadoPor });

            var completa = tarjeta.SellosActuales >= programa.SellosNecesarios;
            if (completa)
            {
                var msg = $"¡Completaste tu tarjeta en {comercio.Nombre}! Ya podés canjear: {programa.Premio}.";
                await notificador.EnviarAsync(cliente, comercio, TipoAviso.TarjetaCompleta, msg);
                db.Avisos.Add(new Aviso { ClienteId = cliente.Id, ComercioId = comercioId, Tipo = TipoAviso.TarjetaCompleta, Mensaje = msg });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new SelloSumado(tarjeta.Id, tarjeta.SellosActuales, programa.SellosNecesarios, completa, Niveles.Calcular(tarjeta.SellosTotales), nueva));
        });

        // El cliente muestra su código de canje y la caja lo marca como entregado.
        g.MapPost("/{comercioId:int}/canjes/{codigo}/entregar", async (int comercioId, string codigo, AppDbContext db) =>
        {
            var canje = await db.Canjes.Include(c => c.Tarjeta).ThenInclude(t => t!.Cliente)
                .FirstOrDefaultAsync(c => c.Codigo == codigo.Trim().ToUpperInvariant());
            if (canje is null || canje.Tarjeta!.ComercioId != comercioId) return Results.NotFound(new { error = "Ese código no existe en este comercio." });
            if (canje.EntregadoEn is not null) return Results.Conflict(new { error = $"Ese premio ya se entregó el {canje.EntregadoEn:dd/MM HH:mm}." });
            if (canje.VenceEn < DateTime.UtcNow) return Results.Conflict(new { error = $"El código venció el {canje.VenceEn:dd/MM}." });

            canje.EntregadoEn = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new CanjeEntregado(canje.Codigo, canje.Premio, canje.Tarjeta.Cliente!.Nombre));
        });

        // Clientes que vienen menos que su ritmo habitual, los más urgentes primero.
        g.MapGet("/{comercioId:int}/clientes-en-riesgo", async (int comercioId, AppDbContext db) =>
        {
            var ahora = DateTime.UtcNow;
            var tarjetas = await db.Tarjetas.Include(t => t.Cliente).Include(t => t.Movimientos)
                .Where(t => t.ComercioId == comercioId).ToListAsync();
            return tarjetas
                .Select(t => (t, r: Enfriamiento.Evaluar(Visitas(t), ahora)))
                .Where(x => x.r.Estado is EstadoCliente.EnRiesgo or EstadoCliente.Critico)
                .OrderByDescending(x => x.r.Relacion)
                .Select(x => new ClienteEnfriandose(x.t.ClienteId, x.t.Cliente!.Nombre, x.t.Cliente.Telefono, x.r.DiasSinVenir, x.r.CadaCuantosDias, x.r.Estado, x.t.AceptaPromos))
                .ToList();
        });

        // Cuánto cuesta la tarjeta por mes. Sin sellos/costo usa los del programa actual.
        g.MapGet("/{comercioId:int}/costo-premio", async (int comercioId, decimal ticketPromedio, int comprasConSelloPorMes,
            int? sellosNecesarios, decimal? costoPremio, AppDbContext db) =>
        {
            var p = await db.Programas.FirstOrDefaultAsync(x => x.ComercioId == comercioId);
            if (p is null && (sellosNecesarios is null || costoPremio is null)) return Results.NotFound(new { error = "El comercio no tiene programa; pasá sellosNecesarios y costoPremio." });
            var sellos = sellosNecesarios ?? p!.SellosNecesarios;
            if (sellos < 1) return Results.BadRequest(new { error = "sellosNecesarios tiene que ser al menos 1." });
            return Results.Ok(CalculadoraCosto.Calcular(sellos, costoPremio ?? p!.CostoPremio, ticketPromedio, comprasConSelloPorMes));
        });

        g.MapGet("/{comercioId:int}/cumpleanos", async (int comercioId, int? dias, AppDbContext db, RelojLocal reloj) =>
        {
            var hoy = reloj.Hoy;
            var rango = Math.Clamp(dias ?? 7, 0, 60);
            var clientes = await db.Tarjetas.Include(t => t.Cliente)
                .Where(t => t.ComercioId == comercioId && t.Cliente!.FechaNacimiento != null)
                .Select(t => t.Cliente!).ToListAsync();
            return clientes
                .Select(c => (c, prox: ProximoCumple(c.FechaNacimiento!.Value, hoy)))
                .Where(x => x.prox.DayNumber - hoy.DayNumber <= rango)
                .OrderBy(x => x.prox)
                .Select(x => new Cumpleanero(x.c.Id, x.c.Nombre, x.prox, x.prox.DayNumber - hoy.DayNumber))
                .ToList();
        });

        g.MapPut("/{comercioId:int}/programa", async (int comercioId, ProgramaPedido pedido, AppDbContext db) =>
        {
            if (pedido.SellosNecesarios is < 3 or > 30) return Results.BadRequest(new { error = "La tarjeta tiene que pedir entre 3 y 30 sellos." });
            if (string.IsNullOrWhiteSpace(pedido.Premio)) return Results.BadRequest(new { error = "Escribí cuál es el premio." });
            var p = await db.Programas.FirstOrDefaultAsync(x => x.ComercioId == comercioId);
            if (p is null) return Results.NotFound();
            p.SellosNecesarios = pedido.SellosNecesarios;
            p.Premio = pedido.Premio.Trim();
            p.CostoPremio = pedido.CostoPremio;
            p.DiasParaRetirar = Math.Clamp(pedido.DiasParaRetirar, 1, 365);
            p.HorasEntreSellos = Math.Clamp(pedido.HorasEntreSellos, 0, 72);
            await db.SaveChangesAsync();
            return Results.Ok(new ProgramaPedido(p.SellosNecesarios, p.Premio, p.CostoPremio, p.DiasParaRetirar, p.HorasEntreSellos));
        });
    }

    internal static List<DateTime> Visitas(Tarjeta t) =>
        t.Movimientos.Where(m => m.Tipo == TipoMovimiento.Sello).Select(m => m.Fecha).ToList();

    internal static async Task<Cliente?> BuscarCliente(AppDbContext db, string q)
    {
        var texto = q.Trim();
        var digitos = Codigos.SoloDigitos(texto);
        // Teléfonos argentinos: se aceptan con o sin 54 / 9 adelante.
        var tel = digitos.StartsWith("549") ? digitos[3..] : digitos.StartsWith("54") ? digitos[2..] : digitos;
        return await db.Clientes.FirstOrDefaultAsync(c =>
            c.CodigoQr == texto || (digitos.Length >= 6 && (c.Dni == digitos || c.Telefono == tel)));
    }

    static DateOnly ProximoCumple(DateOnly nacimiento, DateOnly hoy)
    {
        DateOnly EnAnio(int anio) => nacimiento.Month == 2 && nacimiento.Day == 29 && !DateTime.IsLeapYear(anio)
            ? new DateOnly(anio, 2, 28) : new DateOnly(anio, nacimiento.Month, nacimiento.Day);
        var esteAnio = EnAnio(hoy.Year);
        return esteAnio >= hoy ? esteAnio : EnAnio(hoy.Year + 1);
    }
}

using System.Security.Claims;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Dominio;
using BilleteraSellos.Api.Seguridad;
using BilleteraSellos.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Endpoints;

/// <summary>Registro desde la billetera: el teléfono sale del código ya verificado, no del pedido.</summary>
public record RegistroPedido(string Nombre, string Dni, DateOnly? FechaNacimiento);
public record ClienteRegistrado(int Id, string Nombre, string Token, DateTime VenceEn);
public record CanjePendiente(string Codigo, string Premio, DateTime VenceEn);
public record TarjetaEnBilletera(
    int TarjetaId, int ComercioId, string Comercio, string Rubro, string Direccion, string Color,
    int SellosActuales, int SellosNecesarios, string Premio, bool Completa,
    Nivel Nivel, int SellosTotales, int? FaltanParaSiguienteNivel,
    bool AceptaPromos, string? ResenaGoogleUrl, List<CanjePendiente> CanjesPendientes);
public record Billetera(int ClienteId, string Nombre, string CodigoQr, List<TarjetaEnBilletera> Tarjetas);
public record SumarTarjetaPedido(string CodigoComercio);
public record PromosPedido(bool Acepta);

public static class ClienteEndpoints
{
    public static void MapClienteEndpoints(this IEndpointRouteBuilder app)
    {
        // Paso final del alta: requiere el token que da /api/auth/clientes/verificar a un teléfono sin cuenta.
        app.MapPost("/api/clientes", async (RegistroPedido p, ClaimsPrincipal yo, AppDbContext db, RelojLocal reloj, Sesiones sesiones) =>
        {
            var tel = yo.FindFirstValue(Reclamos.Telefono) ?? "";
            if (Validar(p.Nombre, p.Dni, tel, p.FechaNacimiento, reloj.Hoy) is { } error) return Results.BadRequest(new { error });
            var dni = Codigos.SoloDigitos(p.Dni);
            if (await db.Clientes.AnyAsync(c => c.Dni == dni || c.Telefono == tel))
                return Results.Conflict(new { error = "Ya hay una cuenta con ese DNI o teléfono." });

            var c = new Cliente { Nombre = p.Nombre.Trim(), Dni = dni, Telefono = tel, FechaNacimiento = p.FechaNacimiento };
            db.Clientes.Add(c);
            await db.SaveChangesAsync();

            if (yo.Entero(Reclamos.SesionId) is { } registro) await sesiones.CerrarAsync(registro);
            var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: c.Id);
            return Results.Created("/api/billetera", new ClienteRegistrado(c.Id, c.Nombre, token, vence));
        }).WithTags("Cliente").RequireAuthorization(Politicas.Registro).RequireRateLimiting("auth");

        // La billetera siempre es la del cliente logueado: no hay id en la URL que se pueda cambiar.
        var g = app.MapGroup("/api/billetera").WithTags("Cliente").RequireAuthorization(Politicas.Cliente);

        g.MapGet("/", async (ClaimsPrincipal yo, AppDbContext db) =>
        {
            var clienteId = yo.Entero(Reclamos.ClienteId)!.Value;
            var c = await db.Clientes.FirstOrDefaultAsync(x => x.Id == clienteId);
            if (c is null) return Results.NotFound();
            var ahora = DateTime.UtcNow;
            var tarjetas = await db.Tarjetas
                .Include(t => t.Comercio).ThenInclude(co => co!.Programa)
                .Include(t => t.Canjes)
                .Where(t => t.ClienteId == clienteId)
                .ToListAsync();

            var items = tarjetas
                // Primero las que tienen premio listo o están más cerca de completarse.
                .OrderByDescending(t => t.Canjes.Any(k => k.EntregadoEn == null && k.VenceEn > ahora))
                .ThenByDescending(t => (double)t.SellosActuales / Math.Max(1, t.Comercio!.Programa!.SellosNecesarios))
                .Select(t =>
                {
                    var co = t.Comercio!;
                    var p = co.Programa!;
                    return new TarjetaEnBilletera(t.Id, co.Id, co.Nombre, co.Rubro, co.Direccion, co.Color,
                        t.SellosActuales, p.SellosNecesarios, p.Premio, t.SellosActuales >= p.SellosNecesarios,
                        Niveles.Calcular(t.SellosTotales), t.SellosTotales, Niveles.FaltanParaSiguiente(t.SellosTotales),
                        t.AceptaPromos, co.ResenaGoogleUrl,
                        t.Canjes.Where(k => k.EntregadoEn == null && k.VenceEn > ahora)
                            .Select(k => new CanjePendiente(k.Codigo, k.Premio, k.VenceEn)).ToList());
                }).ToList();

            return Results.Ok(new Billetera(c.Id, c.Nombre, c.CodigoQr, items));
        });

        // El cliente escanea el QR del mostrador y la tarjeta de ese comercio aparece en su billetera.
        g.MapPost("/tarjetas", async (SumarTarjetaPedido p, ClaimsPrincipal yo, AppDbContext db) =>
        {
            var clienteId = yo.Entero(Reclamos.ClienteId)!.Value;
            var comercio = await db.Comercios.FirstOrDefaultAsync(c => c.Codigo == (p.CodigoComercio ?? "").Trim().ToLowerInvariant());
            if (comercio is null) return Results.NotFound(new { error = "No encontramos ese comercio. Revisá el código del QR." });

            var existente = await db.Tarjetas.FirstOrDefaultAsync(t => t.ClienteId == clienteId && t.ComercioId == comercio.Id);
            if (existente is not null) return Results.Ok(new { tarjetaId = existente.Id, yaLaTenias = true });

            var t = new Tarjeta { ClienteId = clienteId, ComercioId = comercio.Id };
            db.Tarjetas.Add(t);
            await db.SaveChangesAsync();
            return Results.Created("/api/billetera", new { tarjetaId = t.Id, yaLaTenias = false });
        });

        // Canjear el premio de una tarjeta completa: descuenta los sellos y genera el código para la caja.
        g.MapPost("/tarjetas/{tarjetaId:int}/canjear", async (int tarjetaId, ClaimsPrincipal yo, AppDbContext db) =>
        {
            var clienteId = yo.Entero(Reclamos.ClienteId)!.Value;
            var t = await db.Tarjetas.Include(x => x.Comercio).ThenInclude(co => co!.Programa)
                .FirstOrDefaultAsync(x => x.Id == tarjetaId && x.ClienteId == clienteId);
            if (t is null) return Results.NotFound(new { error = "Esa tarjeta no está en tu billetera." });
            var p = t.Comercio!.Programa!;
            if (t.SellosActuales < p.SellosNecesarios)
                return Results.Conflict(new { error = $"Todavía te faltan {p.SellosNecesarios - t.SellosActuales} sellos." });

            string codigo;
            do codigo = Codigos.Generar(); while (await db.Canjes.AnyAsync(k => k.Codigo == codigo));

            var canje = new Canje { TarjetaId = t.Id, Codigo = codigo, Premio = p.Premio, VenceEn = DateTime.UtcNow.AddDays(p.DiasParaRetirar) };
            t.SellosActuales -= p.SellosNecesarios;
            t.Canjes.Add(canje);
            t.Movimientos.Add(new Movimiento { Tipo = TipoMovimiento.Canje, CargadoPor = "cliente" });
            await db.SaveChangesAsync();
            return Results.Ok(new CanjePendiente(canje.Codigo, canje.Premio, canje.VenceEn));
        });

        // El cliente decide, comercio por comercio, si quiere recibir promos.
        g.MapPut("/tarjetas/{tarjetaId:int}/promos", async (int tarjetaId, PromosPedido p, ClaimsPrincipal yo, AppDbContext db) =>
        {
            var clienteId = yo.Entero(Reclamos.ClienteId)!.Value;
            var t = await db.Tarjetas.FirstOrDefaultAsync(x => x.Id == tarjetaId && x.ClienteId == clienteId);
            if (t is null) return Results.NotFound(new { error = "Esa tarjeta no está en tu billetera." });
            t.AceptaPromos = p.Acepta;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Valida los datos de un cliente. Devuelve el motivo del error, o null si están bien.</summary>
    internal static string? Validar(string? nombre, string? dni, string telefono, DateOnly? nacimiento, DateOnly hoy)
    {
        var d = Codigos.SoloDigitos(dni ?? "");
        if (string.IsNullOrWhiteSpace(nombre)) return "Falta el nombre.";
        if (d.Length is < 7 or > 9) return "El DNI tiene que tener entre 7 y 9 números.";
        if (telefono.Length is < 8 or > 13) return "El teléfono tiene que tener entre 8 y 13 números, con código de área.";
        if (nacimiento is { } f && (f > hoy || f.Year < 1900)) return "La fecha de nacimiento no es válida.";
        return null;
    }
}

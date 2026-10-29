using System.Security.Claims;
using BilleteraSellos.Aplicacion.Funcionalidades.Billetera;

namespace BilleteraSellos.Api.Endpoints;

/// <summary>Registro desde la billetera: el teléfono sale del código ya verificado, no del pedido.</summary>
public record RegistroPedido(string? Nombre, string? Dni, DateOnly? FechaNacimiento);
public record SumarTarjetaPedido(string? CodigoComercio);
public record PromosPedido(bool Acepta);

/// <summary>Lo que usa el cliente: registro y billetera.</summary>
public static class BilleteraEndpoints
{
    public static void MapBilleteraEndpoints(this IEndpointRouteBuilder app)
    {
        // Paso final del alta: requiere el token que da /api/auth/clientes/verificar a un teléfono sin cuenta.
        app.MapPost("/api/clientes", async (RegistroPedido p, ClaimsPrincipal yo, RegistrarClienteHandler h, CancellationToken ct) =>
            (await h.Handle(new RegistrarCliente(yo.FindFirstValue(Reclamos.Telefono) ?? "", yo.Entero(Reclamos.SesionId), p.Nombre, p.Dni, p.FechaNacimiento), ct))
                .ARespuesta(c => Results.Created("/api/billetera", c)))
            .WithTags("Cliente").RequireAuthorization(Politicas.Registro).RequireRateLimiting("auth");

        // La billetera siempre es la del cliente logueado: no hay id en la URL que se pueda cambiar.
        var g = app.MapGroup("/api/billetera").WithTags("Cliente").RequireAuthorization(Politicas.Cliente);

        g.MapGet("/", async (ClaimsPrincipal yo, ObtenerBilleteraHandler h, CancellationToken ct) =>
            (await h.Handle(new ObtenerBilletera(ClienteId(yo)), ct)).ARespuesta());

        // El cliente escanea el QR del mostrador y la tarjeta de ese comercio aparece en su billetera.
        g.MapPost("/tarjetas", async (SumarTarjetaPedido p, ClaimsPrincipal yo, SumarTarjetaHandler h, CancellationToken ct) =>
            (await h.Handle(new SumarTarjeta(ClienteId(yo), p.CodigoComercio), ct))
                .ARespuesta(t => t.YaLaTenias ? Results.Ok(t) : Results.Created("/api/billetera", t)));

        g.MapPost("/tarjetas/{tarjetaId:int}/canjear", async (int tarjetaId, ClaimsPrincipal yo, CanjearPremioHandler h, CancellationToken ct) =>
            (await h.Handle(new CanjearPremio(ClienteId(yo), tarjetaId), ct)).ARespuesta());

        // El cliente escaneó el QR de una compra: se le suman los sellos.
        g.MapPost("/compras/{codigo}", async (string codigo, ClaimsPrincipal yo, UsarQrCompraHandler h, CancellationToken ct) =>
            (await h.Handle(new UsarQrCompra(ClienteId(yo), codigo), ct)).ARespuesta())
            .RequireRateLimiting("auth");

        g.MapPut("/tarjetas/{tarjetaId:int}/promos", async (int tarjetaId, PromosPedido p, ClaimsPrincipal yo, CambiarPromosHandler h, CancellationToken ct) =>
            (await h.Handle(new CambiarPromos(ClienteId(yo), tarjetaId, p.Acepta), ct)).ARespuesta(Results.NoContent));
    }

    static int ClienteId(ClaimsPrincipal yo) => yo.Entero(Reclamos.ClienteId)!.Value;
}

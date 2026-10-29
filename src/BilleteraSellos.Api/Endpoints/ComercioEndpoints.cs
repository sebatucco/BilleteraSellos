using System.Security.Claims;
using BilleteraSellos.Aplicacion.Funcionalidades.Comercios;
using BilleteraSellos.Dominio.Reglas;

namespace BilleteraSellos.Api.Endpoints;

public record SumarSelloPedido(int ClienteId, decimal? Monto);
public record CompraPedido(decimal? Monto);
public record AltaEnCajaPedido(string? Nombre, string? Dni, string? Telefono, DateOnly? FechaNacimiento);
public record ProgramaPedido(int SellosNecesarios, string? Premio, decimal CostoPremio, int DiasParaRetirar, int HorasEntreSellos,
    decimal? MontoPorSello, decimal? CompraMinima);

/// <summary>Lo que usa el comercio: la caja y el panel.</summary>
public static class ComercioEndpoints
{
    public static void MapComercioEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/comercios").WithTags("Comercio");

        // Directorio de comercios adheridos (público: lo usa la billetera para "cerca tuyo").
        g.MapGet("/", (DirectorioComerciosHandler h, CancellationToken ct) => h.Handle(new DirectorioComercios(), ct));

        // Todo lo demás es del panel: hace falta iniciar sesión y cada usuario solo opera sobre su comercio.
        var priv = g.MapGroup("").RequireAuthorization(Politicas.Comercio).AddEndpointFilter<SoloSuComercio>();

        priv.MapGet("/{comercioId:int}/resumen", async (int comercioId, ResumenComercioHandler h, CancellationToken ct) =>
            (await h.Handle(new ResumenComercio(comercioId), ct)).ARespuesta());

        // Buscar en la caja por DNI, teléfono o el código del QR de la billetera.
        priv.MapGet("/{comercioId:int}/clientes/buscar", async (int comercioId, string q, BuscarClienteEnCajaHandler h, CancellationToken ct) =>
            (await h.Handle(new BuscarClienteEnCaja(comercioId, q), ct)).ARespuesta());

        // Alta en la caja para quien no tiene la app.
        priv.MapPost("/{comercioId:int}/clientes", async (int comercioId, AltaEnCajaPedido p, AltaClienteEnCajaHandler h, CancellationToken ct) =>
            (await h.Handle(new AltaClienteEnCaja(comercioId, p.Nombre, p.Dni, p.Telefono, p.FechaNacimiento), ct))
                .ARespuesta(c => Results.Created($"/api/comercios/{comercioId}/clientes/buscar?q={Codigos.SoloDigitos(p.Dni)}", c)));

        priv.MapPost("/{comercioId:int}/sellos", async (int comercioId, SumarSelloPedido p, ClaimsPrincipal u, SumarSelloHandler h, CancellationToken ct) =>
            (await h.Handle(new SumarSello(comercioId, p.ClienteId, u.Identity?.Name, p.Monto), ct)).ARespuesta());

        // QR de la compra: la caja escribe el monto y el cliente escanea el QR con su billetera (pague como pague).
        priv.MapPost("/{comercioId:int}/compras", async (int comercioId, CompraPedido? p, ClaimsPrincipal u, GenerarQrCompraHandler h, CancellationToken ct) =>
            (await h.Handle(new GenerarQrCompra(comercioId, u.Entero(Reclamos.UsuarioId)!.Value, p?.Monto), ct))
                .ARespuesta(q => Results.Created($"/api/comercios/{comercioId}/compras/{q.Codigo}", q)));

        // La pantalla de la caja pregunta si el cliente ya lo escaneó.
        priv.MapGet("/{comercioId:int}/compras/{codigo}", async (int comercioId, string codigo, ConsultarQrCompraHandler h, CancellationToken ct) =>
            (await h.Handle(new ConsultarQrCompra(comercioId, codigo), ct)).ARespuesta());

        priv.MapPost("/{comercioId:int}/compras/{codigo}/anular", async (int comercioId, string codigo, AnularQrCompraHandler h, CancellationToken ct) =>
            (await h.Handle(new AnularQrCompra(comercioId, codigo), ct)).ARespuesta(Results.NoContent));

        // El cliente muestra su código de canje y la caja lo marca como entregado.
        priv.MapPost("/{comercioId:int}/canjes/{codigo}/entregar", async (int comercioId, string codigo, ClaimsPrincipal u, EntregarCanjeHandler h, CancellationToken ct) =>
            (await h.Handle(new EntregarCanje(comercioId, codigo, u.Identity?.Name), ct)).ARespuesta());

        // Clientes que vienen menos que su ritmo habitual, los más urgentes primero.
        priv.MapGet("/{comercioId:int}/clientes-en-riesgo", (int comercioId, ClientesEnRiesgoHandler h, CancellationToken ct) =>
            h.Handle(new ClientesEnRiesgo(comercioId), ct));

        // Cuánto cuesta la tarjeta por mes. Sin sellos/costo usa los del programa actual.
        priv.MapGet("/{comercioId:int}/costo-premio", async (int comercioId, decimal ticketPromedio, int comprasConSelloPorMes,
            int? sellosNecesarios, decimal? costoPremio, CalcularCostoPremioHandler h, CancellationToken ct) =>
            (await h.Handle(new CalcularCostoPremio(comercioId, ticketPromedio, comprasConSelloPorMes, sellosNecesarios, costoPremio), ct)).ARespuesta());

        priv.MapGet("/{comercioId:int}/cumpleanos", (int comercioId, int? dias, ProximosCumpleanosHandler h, CancellationToken ct) =>
            h.Handle(new ProximosCumpleanos(comercioId, dias), ct));

        priv.MapGet("/{comercioId:int}/programa", async (int comercioId, ObtenerProgramaHandler h, CancellationToken ct) =>
            (await h.Handle(new ObtenerPrograma(comercioId), ct)).ARespuesta());

        // Cambiar las reglas de la tarjeta: solo el dueño.
        priv.MapPut("/{comercioId:int}/programa", async (int comercioId, ProgramaPedido p, ActualizarProgramaHandler h, CancellationToken ct) =>
            (await h.Handle(new ActualizarPrograma(comercioId, p.SellosNecesarios, p.Premio, p.CostoPremio, p.DiasParaRetirar, p.HorasEntreSellos,
                p.MontoPorSello, p.CompraMinima), ct)).ARespuesta())
            .RequireAuthorization(Politicas.Dueno);
    }
}

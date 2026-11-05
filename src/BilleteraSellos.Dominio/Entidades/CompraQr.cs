using BilleteraSellos.Dominio.Comun;

namespace BilleteraSellos.Dominio.Entidades;

public enum EstadoCompraQr { Pendiente, Usada, Vencida, Anulada }

/// <summary>
/// El QR de una compra: la caja escribe el monto, muestra el QR y el cliente lo escanea con su billetera para sumar los sellos.
/// No tiene nada que ver con el medio de pago. Sirve una sola vez y vence a los pocos minutos.
/// El código del QR nunca se guarda: solo su hash, igual que las sesiones.
/// </summary>
public class CompraQr
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(3);

    private CompraQr() { } // EF

    public int Id { get; private set; }
    public int ComercioId { get; private set; }
    /// <summary>Usuario de la caja que generó el QR.</summary>
    public int UsuarioId { get; private set; }
    public string CodigoHash { get; private set; } = "";
    public decimal? Monto { get; private set; }
    /// <summary>Sellos que da la compra, calculados con las reglas vigentes al generar el QR.</summary>
    public int Sellos { get; private set; }
    public DateTime CreadoEn { get; private set; }
    public DateTime VenceEn { get; private set; }
    public DateTime? UsadaEn { get; private set; }
    public int? ClienteId { get; private set; }
    public DateTime? AnuladaEn { get; private set; }

    public static CompraQr Generar(int comercioId, int usuarioId, string codigoHash, decimal? monto, int sellos, DateTime ahora) => new()
    {
        ComercioId = comercioId, UsuarioId = usuarioId, CodigoHash = codigoHash, Monto = monto, Sellos = sellos,
        CreadoEn = ahora, VenceEn = ahora.Add(Vigencia),
    };

    /// <summary>Usada y anulada mandan sobre el vencimiento. En el instante exacto del vencimiento ya no vale.</summary>
    public EstadoCompraQr Estado(DateTime ahora) =>
        UsadaEn is not null ? EstadoCompraQr.Usada
        : AnuladaEn is not null ? EstadoCompraQr.Anulada
        : VenceEn <= ahora ? EstadoCompraQr.Vencida
        : EstadoCompraQr.Pendiente;

    /// <summary>Si el cliente todavía puede usar el QR. No lo marca como usado.</summary>
    public Resultado PuedeUsarse(DateTime ahora) => Estado(ahora) switch
    {
        EstadoCompraQr.Usada => Error.Conflicto("Ese QR ya se usó."),
        EstadoCompraQr.Vencida => Error.Conflicto("Ese QR venció. Pedile al cajero uno nuevo."),
        EstadoCompraQr.Anulada => Error.Conflicto("Ese QR fue anulado. Pedile al cajero uno nuevo."),
        _ => Resultado.Ok(),
    };

    /// <summary>El cliente usa el QR: queda registrado quién y cuándo, y no sirve más.</summary>
    public Resultado Usar(int clienteId, DateTime ahora)
    {
        if (PuedeUsarse(ahora) is { Exito: false } no) return no;
        UsadaEn = ahora;
        ClienteId = clienteId;
        return Resultado.Ok();
    }

    /// <summary>La caja anula el QR (por ejemplo, se equivocó en el monto). Un QR ya usado no se puede anular.</summary>
    public Resultado Anular(DateTime ahora)
    {
        if (UsadaEn is not null) return Error.Conflicto("Ese QR ya se usó: no se puede anular.");
        AnuladaEn ??= ahora;
        return Resultado.Ok();
    }
}

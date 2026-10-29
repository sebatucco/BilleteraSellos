using BilleteraSellos.Dominio.Comun;
using BilleteraSellos.Dominio.Reglas;

namespace BilleteraSellos.Dominio.Entidades;

/// <summary>La tarjeta de un cliente en un comercio: el avance de sellos y el nivel.</summary>
public class Tarjeta
{
    private Tarjeta() { } // EF

    public int Id { get; private set; }
    public int ClienteId { get; private set; }
    public Cliente? Cliente { get; private set; }
    public int ComercioId { get; private set; }
    public Comercio? Comercio { get; private set; }

    /// <summary>Sellos de la vuelta actual; vuelve a 0 al canjear.</summary>
    public int SellosActuales { get; private set; }
    /// <summary>Sellos de toda la historia; define el nivel.</summary>
    public int SellosTotales { get; private set; }
    /// <summary>Consentimiento del cliente para recibir promos de este comercio.</summary>
    public bool AceptaPromos { get; private set; } = true;
    public DateTime CreadaEn { get; private set; } = DateTime.UtcNow;
    public DateTime? UltimoSello { get; private set; }

    public List<Movimiento> Movimientos { get; private set; } = new();
    public List<Canje> Canjes { get; private set; } = new();

    public Nivel Nivel => Niveles.Calcular(SellosTotales);

    public static Tarjeta Abrir(int clienteId, int comercioId, DateTime ahora) =>
        new() { ClienteId = clienteId, ComercioId = comercioId, CreadaEn = ahora };

    public static Tarjeta Abrir(Cliente cliente, Comercio comercio, DateTime ahora) =>
        new() { Cliente = cliente, Comercio = comercio, CreadaEn = ahora };

    /// <summary>
    /// Crea una tarjeta con visitas anteriores (datos de ejemplo o pasar tarjetas de papel al sistema).
    /// Los sellos actuales son el resto de las vueltas completas, como si los premios ya se hubieran canjeado.
    /// </summary>
    public static Tarjeta ImportarHistorial(Cliente cliente, Comercio comercio, IEnumerable<DateTime> visitas, int sellosNecesarios, string cargadoPor)
    {
        var fechas = visitas.OrderBy(f => f).ToList();
        var t = new Tarjeta
        {
            Cliente = cliente,
            Comercio = comercio,
            SellosTotales = fechas.Count,
            SellosActuales = sellosNecesarios > 0 ? fechas.Count % sellosNecesarios : fechas.Count,
            UltimoSello = fechas.Count > 0 ? fechas[^1] : null,
            CreadaEn = fechas.Count > 0 ? fechas[0] : DateTime.UtcNow,
        };
        foreach (var f in fechas) t.Movimientos.Add(Movimiento.Nuevo(TipoMovimiento.Sello, f, cargadoPor));
        return t;
    }

    public bool EstaCompleta(ProgramaSellos programa) => SellosActuales >= programa.SellosNecesarios;

    /// <summary>Premios que se pueden canjear ahora (los sellos que sobran quedan para la próxima vuelta).</summary>
    public int PremiosDisponibles(ProgramaSellos programa) =>
        programa.SellosNecesarios > 0 ? SellosActuales / programa.SellosNecesarios : 0;

    /// <summary>Suma un sello por una compra, sin monto. Atajo de <see cref="SumarSellos"/>.</summary>
    public Resultado SumarSello(ProgramaSellos programa, DateTime ahora, string? cargadoPor) =>
        SumarSellos(programa, 1, null, ahora, cargadoPor);

    /// <summary>
    /// Suma los sellos de una compra. Los que pasan del premio quedan guardados para la vuelta siguiente.
    /// No suma si el último sello es muy reciente (regla de horas entre sellos).
    /// </summary>
    public Resultado SumarSellos(ProgramaSellos programa, int cantidad, decimal? monto, DateTime ahora, string? cargadoPor)
    {
        if (cantidad < 1) return Error.Validacion("La cantidad de sellos tiene que ser al menos 1.");
        if (UltimoSello is { } ultimo && ahora - ultimo < TimeSpan.FromHours(programa.HorasEntreSellos))
            return Error.Conflicto($"Ya se sumó un sello hace menos de {programa.HorasEntreSellos} h. Si es una compra nueva, esperá o cambiá la regla del programa.");

        SellosActuales += cantidad;
        SellosTotales += cantidad;
        UltimoSello = ahora;
        // Un movimiento por compra: así las visitas siguen contando compras, no sellos.
        Movimientos.Add(Movimiento.Nuevo(TipoMovimiento.Sello, ahora, cargadoPor, cantidad, monto));
        return Resultado.Ok();
    }

    /// <summary>Si se puede canjear el premio ahora. Sirve para no generar un código de canje en vano.</summary>
    public Resultado PuedeCanjear(ProgramaSellos programa) =>
        EstaCompleta(programa)
            ? Resultado.Ok()
            : Error.Conflicto($"Todavía te faltan {programa.SellosNecesarios - SellosActuales} sellos.");

    /// <summary>Canjea un premio: descuenta los sellos necesarios (el sobrante queda) y genera el canje con el código para la caja.</summary>
    public Resultado<Canje> Canjear(ProgramaSellos programa, string codigo, DateTime ahora)
    {
        if (PuedeCanjear(programa) is { Exito: false } no) return no.Error!;

        var canje = Canje.Nuevo(codigo, programa.Premio, ahora, ahora.AddDays(programa.DiasParaRetirar));
        SellosActuales -= programa.SellosNecesarios;
        Canjes.Add(canje);
        Movimientos.Add(Movimiento.Nuevo(TipoMovimiento.Canje, ahora, "cliente"));
        return canje;
    }

    public void CambiarPromos(bool acepta) => AceptaPromos = acepta;

    /// <summary>Fechas de las visitas (cada sello sumado), para medir el ritmo del cliente.</summary>
    public List<DateTime> Visitas() =>
        Movimientos.Where(m => m.Tipo == TipoMovimiento.Sello).Select(m => m.Fecha).ToList();
}

public enum TipoMovimiento { Sello, Canje, Anulacion }

/// <summary>Registro de auditoría: cada sello y cada canje, con quién lo hizo.</summary>
public class Movimiento
{
    private Movimiento() { } // EF

    public int Id { get; private set; }
    public int TarjetaId { get; private set; }
    public Tarjeta? Tarjeta { get; private set; }
    public TipoMovimiento Tipo { get; private set; }
    public DateTime Fecha { get; private set; } = DateTime.UtcNow;
    public string? CargadoPor { get; private set; }
    /// <summary>Sellos que sumó este movimiento (una compra puede dar varios).</summary>
    public int Cantidad { get; private set; } = 1;
    /// <summary>Monto de la compra, si la caja lo cargó.</summary>
    public decimal? Monto { get; private set; }

    internal static Movimiento Nuevo(TipoMovimiento tipo, DateTime fecha, string? cargadoPor, int cantidad = 1, decimal? monto = null) =>
        new() { Tipo = tipo, Fecha = fecha, CargadoPor = cargadoPor, Cantidad = cantidad, Monto = monto };
}

/// <summary>Un premio canjeado, con el código que el cliente muestra en la caja.</summary>
public class Canje
{
    private Canje() { } // EF

    public int Id { get; private set; }
    public int TarjetaId { get; private set; }
    public Tarjeta? Tarjeta { get; private set; }
    public string Codigo { get; private set; } = "";
    public string Premio { get; private set; } = "";
    public DateTime CreadoEn { get; private set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; private set; }
    public DateTime? EntregadoEn { get; private set; }
    public string? EntregadoPor { get; private set; }

    public bool EstaPendiente(DateTime ahora) => EntregadoEn == null && VenceEn > ahora;

    internal static Canje Nuevo(string codigo, string premio, DateTime ahora, DateTime venceEn) =>
        new() { Codigo = codigo, Premio = premio, CreadoEn = ahora, VenceEn = venceEn };

    /// <summary>La caja entrega el premio. Un código ya usado o vencido no pasa.</summary>
    public Resultado Entregar(DateTime ahora, string? entregadoPor)
    {
        if (EntregadoEn is not null) return Error.Conflicto($"Ese premio ya se entregó el {EntregadoEn:dd/MM HH:mm}.");
        // Mismo criterio que EstaPendiente: en el instante exacto del vencimiento ya no vale.
        if (VenceEn <= ahora) return Error.Conflicto($"El código venció el {VenceEn:dd/MM}.");
        EntregadoEn = ahora;
        EntregadoPor = entregadoPor;
        return Resultado.Ok();
    }
}

public enum TipoAviso { Cumpleanos, Reactivacion, TarjetaCompleta, Promo }

/// <summary>Cada aviso enviado a un cliente. Sirve para no repetir y para medir resultados.</summary>
public class Aviso
{
    private Aviso() { } // EF

    public int Id { get; private set; }
    public int ClienteId { get; private set; }
    public int ComercioId { get; private set; }
    public TipoAviso Tipo { get; private set; }
    public string Mensaje { get; private set; } = "";
    public DateTime EnviadoEn { get; private set; } = DateTime.UtcNow;

    public static Aviso Crear(int clienteId, int comercioId, TipoAviso tipo, string mensaje, DateTime ahora) =>
        new() { ClienteId = clienteId, ComercioId = comercioId, Tipo = tipo, Mensaje = mensaje, EnviadoEn = ahora };
}

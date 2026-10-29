using BilleteraSellos.Dominio.Comun;
using BilleteraSellos.Dominio.Reglas;

namespace BilleteraSellos.Dominio.Entidades;

/// <summary>Un negocio adherido a la billetera (cafetería, barbería, etc.).</summary>
public class Comercio
{
    public const string ColorPorDefecto = "#2C47D6";

    private Comercio() { } // EF

    public int Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Rubro { get; private set; } = "";
    public string Direccion { get; private set; } = "";
    /// <summary>Código corto que va en el QR del mostrador para que el cliente sume la tarjeta.</summary>
    public string Codigo { get; private set; } = "";
    /// <summary>Color de la tarjeta en la billetera, en hex (#7A4B2A).</summary>
    public string Color { get; private set; } = ColorPorDefecto;
    /// <summary>Link al formulario de reseña de Google. La reseña nunca da sellos ni premios.</summary>
    public string? ResenaGoogleUrl { get; private set; }

    public ProgramaSellos? Programa { get; private set; }
    public List<Tarjeta> Tarjetas { get; private set; } = new();

    public static Comercio Crear(string nombre, string rubro, string direccion, string codigo, string? color, ProgramaSellos programa, string? resenaGoogleUrl = null) => new()
    {
        Nombre = nombre.Trim(),
        Rubro = rubro?.Trim() ?? "",
        Direccion = direccion?.Trim() ?? "",
        Codigo = codigo,
        Color = string.IsNullOrWhiteSpace(color) ? ColorPorDefecto : color,
        ResenaGoogleUrl = resenaGoogleUrl,
        Programa = programa,
    };
}

/// <summary>Las reglas de la tarjeta de sellos de un comercio.</summary>
public class ProgramaSellos
{
    private ProgramaSellos() { } // EF

    public int Id { get; private set; }
    public int ComercioId { get; private set; }
    public Comercio? Comercio { get; private set; }

    public int SellosNecesarios { get; private set; } = 10;
    public string Premio { get; private set; } = "";
    /// <summary>Lo que le cuesta al comercio entregar el premio, para la calculadora de costos.</summary>
    public decimal CostoPremio { get; private set; }
    /// <summary>Días que tiene el cliente para retirar el premio una vez canjeado.</summary>
    public int DiasParaRetirar { get; private set; } = 7;
    /// <summary>Horas mínimas entre dos sellos del mismo cliente (evita sellar dos veces seguidas).</summary>
    public int HorasEntreSellos { get; private set; } = 1;
    /// <summary>"1 sello cada $X". Null = 1 sello por compra, sin importar el monto.</summary>
    public decimal? MontoPorSello { get; private set; }
    /// <summary>Monto mínimo de la compra para sumar sellos. Null = sin mínimo.</summary>
    public decimal? CompraMinima { get; private set; }

    /// <summary>Tope de sellos de una sola compra: frena errores de tipeo en el monto (35.000.000 en vez de 35.000).</summary>
    public const int MaxSellosPorCompra = 30;

    public static ProgramaSellos Nuevo(int sellos, string premio, decimal costo, decimal? montoPorSello = null, decimal? compraMinima = null) =>
        new() { SellosNecesarios = sellos, Premio = premio, CostoPremio = costo, MontoPorSello = montoPorSello, CompraMinima = compraMinima };

    /// <summary>Cambia las reglas de la tarjeta. Días y horas se acotan a valores razonables.</summary>
    public Resultado Actualizar(int sellos, string? premio, decimal costo, int diasParaRetirar, int horasEntreSellos,
        decimal? montoPorSello = null, decimal? compraMinima = null)
    {
        if (sellos is < 3 or > 30) return Error.Validacion("La tarjeta tiene que pedir entre 3 y 30 sellos.");
        if (string.IsNullOrWhiteSpace(premio)) return Error.Validacion("Escribí cuál es el premio.");
        if (montoPorSello is <= 0) return Error.Validacion("El monto por sello tiene que ser mayor a cero.");
        if (compraMinima is < 0) return Error.Validacion("La compra mínima no puede ser negativa.");
        SellosNecesarios = sellos;
        Premio = premio.Trim();
        CostoPremio = costo;
        DiasParaRetirar = Math.Clamp(diasParaRetirar, 1, 365);
        HorasEntreSellos = Math.Clamp(horasEntreSellos, 0, 72);
        MontoPorSello = montoPorSello;
        CompraMinima = compraMinima;
        return Resultado.Ok();
    }

    /// <summary>
    /// Cuántos sellos da una compra según las reglas: 1 por compra, o 1 cada <see cref="MontoPorSello"/>,
    /// siempre que se llegue a la <see cref="CompraMinima"/>. Si el programa depende del monto, el monto es obligatorio.
    /// </summary>
    public Resultado<int> SellosPorCompra(decimal? monto)
    {
        if (monto is < 0) return Error.Validacion("El monto no puede ser negativo.");
        if ((MontoPorSello is not null || CompraMinima is not null) && monto is null)
            return Error.Validacion("Escribí el monto de la compra.");
        if (CompraMinima is { } minimo && monto < minimo)
            return Error.Validacion($"La compra mínima para sumar sellos es de {Pesos.Formatear(minimo)}.");

        var sellos = 1;
        if (MontoPorSello is { } porSello)
        {
            var calculado = Math.Floor(monto!.Value / porSello);
            if (calculado < 1) return Error.Validacion($"Con ese monto no alcanza para un sello (1 sello cada {Pesos.Formatear(porSello)}).");
            if (calculado > MaxSellosPorCompra)
                return Error.Validacion($"Esa compra daría {calculado:0} sellos. Revisá el monto (máximo {MaxSellosPorCompra} por compra).");
            sellos = (int)calculado;
        }
        return sellos;
    }
}

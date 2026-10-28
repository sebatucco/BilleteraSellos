using BilleteraSellos.Dominio.Comun;

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

    public static ProgramaSellos Nuevo(int sellos, string premio, decimal costo) =>
        new() { SellosNecesarios = sellos, Premio = premio, CostoPremio = costo };

    /// <summary>Cambia las reglas de la tarjeta. Días y horas se acotan a valores razonables.</summary>
    public Resultado Actualizar(int sellos, string? premio, decimal costo, int diasParaRetirar, int horasEntreSellos)
    {
        if (sellos is < 3 or > 30) return Error.Validacion("La tarjeta tiene que pedir entre 3 y 30 sellos.");
        if (string.IsNullOrWhiteSpace(premio)) return Error.Validacion("Escribí cuál es el premio.");
        SellosNecesarios = sellos;
        Premio = premio.Trim();
        CostoPremio = costo;
        DiasParaRetirar = Math.Clamp(diasParaRetirar, 1, 365);
        HorasEntreSellos = Math.Clamp(horasEntreSellos, 0, 72);
        return Resultado.Ok();
    }
}

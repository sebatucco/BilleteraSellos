using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Dominio;

/// <summary>Un negocio adherido a la billetera (cafetería, barbería, etc.).</summary>
[Index(nameof(Codigo), IsUnique = true)]
public class Comercio
{
    public int Id { get; set; }
    [MaxLength(120)] public string Nombre { get; set; } = "";
    [MaxLength(60)] public string Rubro { get; set; } = "";
    [MaxLength(200)] public string Direccion { get; set; } = "";
    /// <summary>Código corto que va en el QR del mostrador para que el cliente sume la tarjeta.</summary>
    [MaxLength(40)] public string Codigo { get; set; } = "";
    /// <summary>Color de la tarjeta en la billetera, en hex (#7A4B2A).</summary>
    [MaxLength(9)] public string Color { get; set; } = "#2C47D6";
    /// <summary>Link al formulario de reseña de Google. La reseña nunca da sellos ni premios.</summary>
    [MaxLength(400)] public string? ResenaGoogleUrl { get; set; }

    public ProgramaSellos? Programa { get; set; }
    public List<Tarjeta> Tarjetas { get; set; } = new();
}

/// <summary>Las reglas de la tarjeta de sellos de un comercio.</summary>
[Index(nameof(ComercioId), IsUnique = true)]
public class ProgramaSellos
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public Comercio? Comercio { get; set; }

    public int SellosNecesarios { get; set; } = 10;
    [MaxLength(120)] public string Premio { get; set; } = "";
    /// <summary>Lo que le cuesta al comercio entregar el premio, para la calculadora de costos.</summary>
    public decimal CostoPremio { get; set; }
    /// <summary>Días que tiene el cliente para retirar el premio una vez canjeado.</summary>
    public int DiasParaRetirar { get; set; } = 7;
    /// <summary>Horas mínimas entre dos sellos del mismo cliente (evita sellar dos veces seguidas).</summary>
    public int HorasEntreSellos { get; set; } = 1;
}

/// <summary>Una persona con la billetera. Se registra una sola vez y usa todas las tarjetas.</summary>
[Index(nameof(Dni), IsUnique = true)]
[Index(nameof(Telefono), IsUnique = true)]
[Index(nameof(CodigoQr), IsUnique = true)]
public class Cliente
{
    public int Id { get; set; }
    [MaxLength(120)] public string Nombre { get; set; } = "";
    [MaxLength(20)] public string Dni { get; set; } = "";
    [MaxLength(20)] public string Telefono { get; set; } = "";
    public DateOnly? FechaNacimiento { get; set; }
    /// <summary>Lo que codifica el QR personal de la billetera. No es el DNI, para no exponerlo.</summary>
    [MaxLength(40)] public string CodigoQr { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public List<Tarjeta> Tarjetas { get; set; } = new();
}

/// <summary>La tarjeta de un cliente en un comercio: el avance de sellos y el nivel.</summary>
[Index(nameof(ClienteId), nameof(ComercioId), IsUnique = true)]
public class Tarjeta
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int ComercioId { get; set; }
    public Comercio? Comercio { get; set; }

    /// <summary>Sellos de la vuelta actual; vuelve a 0 al canjear.</summary>
    public int SellosActuales { get; set; }
    /// <summary>Sellos de toda la historia; define el nivel.</summary>
    public int SellosTotales { get; set; }
    /// <summary>Consentimiento del cliente para recibir promos de este comercio.</summary>
    public bool AceptaPromos { get; set; } = true;
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoSello { get; set; }

    public List<Movimiento> Movimientos { get; set; } = new();
    public List<Canje> Canjes { get; set; } = new();
}

public enum TipoMovimiento { Sello, Canje, Anulacion }

/// <summary>Registro de auditoría: cada sello y cada canje, con quién lo hizo.</summary>
public class Movimiento
{
    public int Id { get; set; }
    public int TarjetaId { get; set; }
    public Tarjeta? Tarjeta { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string? CargadoPor { get; set; }
}

/// <summary>Un premio canjeado, con el código que el cliente muestra en la caja.</summary>
[Index(nameof(Codigo), IsUnique = true)]
public class Canje
{
    public int Id { get; set; }
    public int TarjetaId { get; set; }
    public Tarjeta? Tarjeta { get; set; }
    [MaxLength(12)] public string Codigo { get; set; } = "";
    [MaxLength(120)] public string Premio { get; set; } = "";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; set; }
    public DateTime? EntregadoEn { get; set; }
}

public enum TipoAviso { Cumpleanos, Reactivacion, TarjetaCompleta, Promo }

/// <summary>Cada aviso enviado a un cliente. Sirve para no repetir y para medir resultados.</summary>
public class Aviso
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int ComercioId { get; set; }
    public TipoAviso Tipo { get; set; }
    [MaxLength(300)] public string Mensaje { get; set; } = "";
    public DateTime EnviadoEn { get; set; } = DateTime.UtcNow;
}

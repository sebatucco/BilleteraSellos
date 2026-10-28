using BilleteraSellos.Dominio.Comun;
using BilleteraSellos.Dominio.Reglas;

namespace BilleteraSellos.Dominio.Entidades;

/// <summary>Una persona con la billetera. Se registra una sola vez y usa todas las tarjetas.</summary>
public class Cliente
{
    private Cliente() { } // EF

    public int Id { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Dni { get; private set; } = "";
    public string Telefono { get; private set; } = "";
    public DateOnly? FechaNacimiento { get; private set; }
    /// <summary>Lo que codifica el QR personal de la billetera. No es el DNI, para no exponerlo.</summary>
    public string CodigoQr { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTime CreadoEn { get; private set; } = DateTime.UtcNow;

    public List<Tarjeta> Tarjetas { get; private set; } = new();

    /// <summary>
    /// Alta de un cliente. <paramref name="telefonoNormalizado"/> ya tiene que venir normalizado
    /// (ver <see cref="Telefonos.Normalizar"/>); el DNI se limpia acá.
    /// </summary>
    public static Resultado<Cliente> Registrar(string? nombre, string? dni, string telefonoNormalizado, DateOnly? nacimiento, DateOnly hoy)
    {
        if (Validar(nombre, dni, telefonoNormalizado, nacimiento, hoy) is { } error) return Error.Validacion(error);
        return new Cliente
        {
            Nombre = nombre!.Trim(),
            Dni = Codigos.SoloDigitos(dni),
            Telefono = telefonoNormalizado,
            FechaNacimiento = nacimiento,
        };
    }

    /// <summary>Valida los datos de un cliente. Devuelve el motivo del error, o null si están bien.</summary>
    public static string? Validar(string? nombre, string? dni, string telefono, DateOnly? nacimiento, DateOnly hoy)
    {
        var d = Codigos.SoloDigitos(dni);
        if (string.IsNullOrWhiteSpace(nombre)) return "Falta el nombre.";
        if (d.Length is < 7 or > 9) return "El DNI tiene que tener entre 7 y 9 números.";
        if (!Telefonos.EsValido(telefono)) return "El teléfono tiene que tener entre 8 y 13 números, con código de área.";
        if (nacimiento is { } f && (f > hoy || f.Year < 1900)) return "La fecha de nacimiento no es válida.";
        return null;
    }
}

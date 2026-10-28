namespace BilleteraSellos.Dominio.Entidades;

public enum TipoSesion { Comercio, Cliente, Registro }

/// <summary>
/// Sesión abierta. El token que recibe el usuario nunca se guarda: solo su hash,
/// así una copia de la base no sirve para entrar. Se puede revocar en cualquier momento.
/// </summary>
public class Sesion
{
    public static readonly TimeSpan DuracionComercio = TimeSpan.FromDays(7);
    public static readonly TimeSpan DuracionCliente = TimeSpan.FromDays(90);
    public static readonly TimeSpan DuracionRegistro = TimeSpan.FromMinutes(30);

    private Sesion() { } // EF

    public int Id { get; private set; }
    public string TokenHash { get; private set; } = "";
    public TipoSesion Tipo { get; private set; }
    public int? UsuarioId { get; private set; }
    public int? ClienteId { get; private set; }
    /// <summary>Teléfono verificado, para las sesiones de registro.</summary>
    public string? Telefono { get; private set; }
    public DateTime CreadaEn { get; private set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; private set; }
    public DateTime? RevocadaEn { get; private set; }

    public static Sesion Abrir(TipoSesion tipo, string tokenHash, DateTime ahora, int? usuarioId = null, int? clienteId = null, string? telefono = null)
    {
        var dura = tipo switch { TipoSesion.Comercio => DuracionComercio, TipoSesion.Cliente => DuracionCliente, _ => DuracionRegistro };
        return new Sesion
        {
            Tipo = tipo, TokenHash = tokenHash, UsuarioId = usuarioId, ClienteId = clienteId, Telefono = telefono,
            CreadaEn = ahora, VenceEn = ahora.Add(dura),
        };
    }

    public bool EstaVigente(DateTime ahora) => RevocadaEn is null && VenceEn > ahora;

    public void Revocar(DateTime ahora) => RevocadaEn ??= ahora;
}

/// <summary>Código de un solo uso enviado por SMS para que el cliente entre con su teléfono.</summary>
public class CodigoVerificacion
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(10);
    public const int MaxIntentos = 5;
    public const int MaxPorHora = 5;

    private CodigoVerificacion() { } // EF

    public int Id { get; private set; }
    public string Telefono { get; private set; } = "";
    public string CodigoHash { get; private set; } = "";
    public DateTime CreadoEn { get; private set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; private set; }
    public int Intentos { get; private set; }
    public DateTime? UsadoEn { get; private set; }

    public static CodigoVerificacion Crear(string telefono, string codigoHash, DateTime ahora) =>
        new() { Telefono = telefono, CodigoHash = codigoHash, CreadoEn = ahora, VenceEn = ahora.Add(Vigencia) };

    public bool AgotoIntentos => Intentos >= MaxIntentos;

    /// <summary>Un código nuevo anula los anteriores sin usar.</summary>
    public void Anular(DateTime ahora) => UsadoEn ??= ahora;

    public void RegistrarIntento() => Intentos++;

    public void Usar(DateTime ahora) => UsadoEn = ahora;
}

/// <summary>Link de un solo uso para restablecer la contraseña de un usuario del panel. Se guarda solo el hash.</summary>
public class TokenRestablecimiento
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromHours(1);
    public const int MaxPorHora = 3;

    private TokenRestablecimiento() { } // EF

    public int Id { get; private set; }
    public int UsuarioId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTime CreadoEn { get; private set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; private set; }
    public DateTime? UsadoEn { get; private set; }

    public static TokenRestablecimiento Crear(int usuarioId, string tokenHash, DateTime ahora) =>
        new() { UsuarioId = usuarioId, TokenHash = tokenHash, CreadoEn = ahora, VenceEn = ahora.Add(Vigencia) };

    public bool EstaVigente(DateTime ahora) => UsadoEn is null && VenceEn > ahora;

    public void Usar(DateTime ahora) => UsadoEn ??= ahora;
}

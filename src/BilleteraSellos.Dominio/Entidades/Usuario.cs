namespace BilleteraSellos.Dominio.Entidades;

public enum RolUsuario { Dueno, Cajero }

/// <summary>Persona que usa el panel de un comercio: el dueño o un cajero.</summary>
public class Usuario
{
    public const int MaxIntentosLogin = 5;
    public static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);

    private Usuario() { } // EF

    public int Id { get; private set; }
    public int ComercioId { get; private set; }
    public Comercio? Comercio { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public RolUsuario Rol { get; private set; } = RolUsuario.Cajero;
    public bool Activo { get; private set; } = true;
    public int IntentosFallidos { get; private set; }
    public DateTime? BloqueadoHasta { get; private set; }
    public DateTime CreadoEn { get; private set; } = DateTime.UtcNow;

    /// <summary>Usuario de un comercio que se está creando en la misma operación (registro).</summary>
    public static Usuario Crear(Comercio comercio, string nombre, string emailNormalizado, string passwordHash, RolUsuario rol) =>
        new() { Comercio = comercio, Nombre = nombre.Trim(), Email = emailNormalizado, PasswordHash = passwordHash, Rol = rol };

    /// <summary>Usuario de un comercio que ya existe (alta de cajero).</summary>
    public static Usuario Crear(int comercioId, string nombre, string emailNormalizado, string passwordHash, RolUsuario rol) =>
        new() { ComercioId = comercioId, Nombre = nombre.Trim(), Email = emailNormalizado, PasswordHash = passwordHash, Rol = rol };

    public bool EstaBloqueado(DateTime ahora) => BloqueadoHasta is { } hasta && hasta > ahora;

    /// <summary>Contraseña incorrecta: al quinto intento la cuenta se bloquea 15 minutos.</summary>
    public void RegistrarFalla(DateTime ahora)
    {
        IntentosFallidos++;
        if (IntentosFallidos >= MaxIntentosLogin)
        {
            BloqueadoHasta = ahora.Add(Bloqueo);
            IntentosFallidos = 0;
        }
    }

    public void RegistrarIngreso()
    {
        IntentosFallidos = 0;
        BloqueadoHasta = null;
    }

    public void CambiarContrasena(string nuevoHash) => PasswordHash = nuevoHash;

    /// <summary>Contraseña nueva desde el link del email: además desbloquea la cuenta.</summary>
    public void Restablecer(string nuevoHash)
    {
        PasswordHash = nuevoHash;
        RegistrarIngreso();
    }

    public void CambiarActivo(bool activo) => Activo = activo;
}

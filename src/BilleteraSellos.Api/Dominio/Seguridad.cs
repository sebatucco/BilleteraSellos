using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Dominio;

public enum RolUsuario { Dueno, Cajero }

/// <summary>Persona que usa el panel de un comercio: el dueño o un cajero.</summary>
[Index(nameof(Email), IsUnique = true)]
public class Usuario
{
    public int Id { get; set; }
    public int ComercioId { get; set; }
    public Comercio? Comercio { get; set; }
    [MaxLength(120)] public string Nombre { get; set; } = "";
    [MaxLength(200)] public string Email { get; set; } = "";
    [MaxLength(200)] public string PasswordHash { get; set; } = "";
    public RolUsuario Rol { get; set; } = RolUsuario.Cajero;
    public bool Activo { get; set; } = true;
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

public enum TipoSesion { Comercio, Cliente, Registro }

/// <summary>
/// Sesión abierta. El token que recibe el usuario nunca se guarda: solo su hash,
/// así una copia de la base no sirve para entrar. Se puede revocar en cualquier momento.
/// </summary>
[Index(nameof(TokenHash), IsUnique = true)]
public class Sesion
{
    public int Id { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public TipoSesion Tipo { get; set; }
    public int? UsuarioId { get; set; }
    public int? ClienteId { get; set; }
    /// <summary>Teléfono verificado, para las sesiones de registro.</summary>
    [MaxLength(20)] public string? Telefono { get; set; }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; set; }
    public DateTime? RevocadaEn { get; set; }
}

/// <summary>Código de un solo uso enviado por SMS para que el cliente entre con su teléfono.</summary>
[Index(nameof(Telefono))]
public class CodigoVerificacion
{
    public int Id { get; set; }
    [MaxLength(20)] public string Telefono { get; set; } = "";
    [MaxLength(64)] public string CodigoHash { get; set; } = "";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; set; }
    public int Intentos { get; set; }
    public DateTime? UsadoEn { get; set; }
}

/// <summary>Link de un solo uso para restablecer la contraseña de un usuario del panel. Se guarda solo el hash.</summary>
[Index(nameof(TokenHash), IsUnique = true)]
public class TokenRestablecimiento
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime VenceEn { get; set; }
    public DateTime? UsadoEn { get; set; }
}

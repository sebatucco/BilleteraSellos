namespace BilleteraSellos.Aplicacion.Abstracciones;

/// <summary>
/// Acceso a los datos para los casos de uso. Expone las tablas de EF Core directamente:
/// EF ya funciona como repositorio y unidad de trabajo, así que no se duplica esa capa.
/// </summary>
public interface IAppDbContext
{
    DbSet<Comercio> Comercios { get; }
    DbSet<ProgramaSellos> Programas { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Tarjeta> Tarjetas { get; }
    DbSet<Movimiento> Movimientos { get; }
    DbSet<Canje> Canjes { get; }
    DbSet<Aviso> Avisos { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<Sesion> Sesiones { get; }
    DbSet<CodigoVerificacion> CodigosVerificacion { get; }
    DbSet<TokenRestablecimiento> TokensRestablecimiento { get; }
    DbSet<CompraQr> ComprasQr { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>La hora, para poder fijarla en los tests.</summary>
public interface IReloj
{
    /// <summary>Ahora en UTC (lo que se guarda en la base).</summary>
    DateTime UtcAhora { get; }
    /// <summary>La fecha de hoy en la zona horaria del negocio (Argentina), para los cumpleaños.</summary>
    DateOnly HoyLocal { get; }
    /// <summary>La hora en la zona horaria del negocio, para programar tareas (los avisos diarios).</summary>
    DateTime AhoraLocal { get; }
}

/// <summary>Canal por el que le llega un aviso al cliente (hoy la consola; después Wallet, push o WhatsApp).</summary>
public interface INotificador
{
    Task EnviarAsync(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct = default);
}

/// <summary>Canal por el que sale el código de verificación (consola, SMS o WhatsApp).</summary>
public interface IEnviadorCodigos
{
    Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default);
}

/// <summary>Canal de emails (recuperar contraseña y, más adelante, otros avisos al comercio).</summary>
public interface IEnviadorEmail
{
    Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default);
}

/// <summary>Un SMS o email no se pudo enviar. El mensaje es apto para mostrarle al usuario.</summary>
public class EnvioFallidoException(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

public interface IHasherContrasenas
{
    string Hashear(string contrasena);
    bool Verificar(string contrasena, string hashGuardado);
}

/// <summary>Datos de la app que aparecen en mensajes y links.</summary>
public sealed record OpcionesApp(string NombreApp, string UrlPublica);

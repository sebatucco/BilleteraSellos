using BilleteraSellos.Infraestructura.Persistencia;
using BilleteraSellos.Infraestructura.Servicios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BilleteraSellos.Infraestructura;

public static class DependencyInjection
{
    /// <summary>
    /// Registra la base de datos y los servicios externos (reloj, contraseñas, avisos, SMS y email) según la configuración.
    /// Falla al arrancar si un proveedor configurado no tiene sus datos obligatorios.
    /// </summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios, IConfiguration config)
    {
        var (proveedor, conexion) = ResolverBase(config);
        servicios.AddDbContext<AppDbContext>(o =>
        {
            if (proveedor == "Postgres") o.UseNpgsql(conexion);
            else o.UseSqlite(conexion);
        });
        servicios.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        servicios.AddSingleton<IReloj, RelojSistema>();
        servicios.AddSingleton<IHasherContrasenas, HasherPbkdf2>();
        servicios.AddSingleton<INotificador, NotificadorConsola>();
        servicios.AddSingleton(new OpcionesApp(
            config["NombreApp"] ?? "Billetera de Sellos",
            config["UrlPublica"] ?? "http://localhost:5225"));

        AgregarEnvios(servicios, config);
        return servicios;
    }

    /// <summary>
    /// SQLite para desarrollar en la PC, PostgreSQL en el servidor.
    /// Railway/Render/Heroku dan la base en DATABASE_URL: si existe, manda sobre la configuración.
    /// </summary>
    public static (string Proveedor, string Conexion) ResolverBase(IConfiguration config)
    {
        if (config["DATABASE_URL"] is { Length: > 0 } url) return ("Postgres", ConexionBase.DesdeUrl(url));
        var proveedor = (config["BaseDeDatos:Proveedor"] ?? "Sqlite").Equals("Postgres", StringComparison.OrdinalIgnoreCase) ? "Postgres" : "Sqlite";
        return (proveedor, config.GetConnectionString("Principal") ?? "Data Source=billetera.db");
    }

    static void AgregarEnvios(IServiceCollection servicios, IConfiguration c)
    {
        var sms = c["Sms:Proveedor"] ?? "Consola";
        if (sms.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
        {
            Requerir(c, "Sms:Twilio:AccountSid", "Sms:Twilio:AuthToken", "Sms:Twilio:From");
            servicios.AddHttpClient<IEnviadorCodigos, EnviadorCodigosTwilio>(h => h.Timeout = TimeSpan.FromSeconds(15));
        }
        else if (sms.Equals("Consola", StringComparison.OrdinalIgnoreCase))
            servicios.AddSingleton<IEnviadorCodigos, EnviadorCodigosConsola>();
        else
            throw new InvalidOperationException($"Sms:Proveedor \"{sms}\" no existe. Usá \"Consola\" o \"Twilio\".");

        var email = c["Email:Proveedor"] ?? "Consola";
        if (email.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        {
            Requerir(c, "Email:Smtp:Host", "Email:Smtp:De");
            servicios.AddSingleton<IEnviadorEmail, EnviadorEmailSmtp>();
        }
        else if (email.Equals("Consola", StringComparison.OrdinalIgnoreCase))
            servicios.AddSingleton<IEnviadorEmail, EnviadorEmailConsola>();
        else
            throw new InvalidOperationException($"Email:Proveedor \"{email}\" no existe. Usá \"Consola\" o \"Smtp\".");
    }

    static void Requerir(IConfiguration c, params string[] claves)
    {
        var faltan = claves.Where(k => string.IsNullOrWhiteSpace(c[k])).ToList();
        if (faltan.Count > 0) throw new InvalidOperationException("Falta configurar: " + string.Join(", ", faltan));
    }
}

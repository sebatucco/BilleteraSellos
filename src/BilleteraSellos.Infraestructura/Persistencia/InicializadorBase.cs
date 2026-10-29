using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BilleteraSellos.Infraestructura.Persistencia;

/// <summary>Prepara la base al arrancar la app.</summary>
public static class InicializadorBase
{
    /// <summary>
    /// PostgreSQL (producción): aplica las migraciones pendientes.
    /// SQLite (desarrollo en la PC): crea la base si no existe; si es de una versión vieja, la recrea.
    /// En desarrollo, además, carga los datos de ejemplo.
    /// </summary>
    public static async Task InicializarAsync(IServiceProvider servicios, bool esDesarrollo, CancellationToken ct = default)
    {
        using var scope = servicios.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BilleteraSellos.Base");

        if (EsPostgres(db))
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
            if (esDesarrollo)
            {
                // Si la base de desarrollo es de una versión anterior (le faltan tablas), se recrea con los datos de ejemplo.
                try
                {
                    await db.Usuarios.AnyAsync(ct);
                    await db.TokensRestablecimiento.AnyAsync(ct);
                    await db.ComprasQr.AnyAsync(ct);
                    await db.Movimientos.Select(m => m.Cantidad).FirstOrDefaultAsync(ct);
                    await db.Programas.Select(p => p.MontoPorSello).FirstOrDefaultAsync(ct);
                }
                catch (Exception)
                {
                    log.LogWarning("La base de desarrollo era de una versión anterior: se recrea con datos de ejemplo.");
                    await db.Database.EnsureDeletedAsync(ct);
                    await db.Database.EnsureCreatedAsync(ct);
                }
            }
        }

        if (esDesarrollo)
            await DatosDemo.CargarAsync(db, scope.ServiceProvider.GetRequiredService<IHasherContrasenas>(), ct);
    }

    static bool EsPostgres(AppDbContext db) =>
        db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}

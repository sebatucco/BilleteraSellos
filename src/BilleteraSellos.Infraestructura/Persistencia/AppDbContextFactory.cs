using Microsoft.EntityFrameworkCore.Design;

namespace BilleteraSellos.Infraestructura.Persistencia;

/// <summary>
/// Lo usa la herramienta <c>dotnet ef</c> para generar migraciones. Las migraciones son para PostgreSQL
/// (producción); la cadena es ficticia porque generar una migración no se conecta a la base.
/// <code>dotnet ef migrations add Nombre -p src/BilleteraSellos.Infraestructura -s src/BilleteraSellos.Api -o Persistencia/Migraciones</code>
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=billetera_diseno;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(opciones);
    }
}

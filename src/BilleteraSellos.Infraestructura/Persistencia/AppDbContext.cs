namespace BilleteraSellos.Infraestructura.Persistencia;

/// <summary>La base de datos con EF Core. Las tablas y reglas de mapeo están en Configuraciones/.</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<ProgramaSellos> Programas => Set<ProgramaSellos>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Tarjeta> Tarjetas => Set<Tarjeta>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Canje> Canjes => Set<Canje>();
    public DbSet<Aviso> Avisos => Set<Aviso>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Sesion> Sesiones => Set<Sesion>();
    public DbSet<CodigoVerificacion> CodigosVerificacion => Set<CodigoVerificacion>();
    public DbSet<TokenRestablecimiento> TokensRestablecimiento => Set<TokenRestablecimiento>();
    public DbSet<CompraQr> ComprasQr => Set<CompraQr>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}

using BilleteraSellos.Api.Dominio;
using Microsoft.EntityFrameworkCore;

namespace BilleteraSellos.Api.Datos;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Comercio> Comercios => Set<Comercio>();
    public DbSet<ProgramaSellos> Programas => Set<ProgramaSellos>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Tarjeta> Tarjetas => Set<Tarjeta>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Canje> Canjes => Set<Canje>();
    public DbSet<Aviso> Avisos => Set<Aviso>();
}

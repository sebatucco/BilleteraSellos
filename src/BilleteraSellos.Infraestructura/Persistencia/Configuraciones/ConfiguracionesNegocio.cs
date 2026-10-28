using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BilleteraSellos.Infraestructura.Persistencia.Configuraciones;

public class ComercioConfiguracion : IEntityTypeConfiguration<Comercio>
{
    public void Configure(EntityTypeBuilder<Comercio> b)
    {
        b.Property(x => x.Nombre).HasMaxLength(120);
        b.Property(x => x.Rubro).HasMaxLength(60);
        b.Property(x => x.Direccion).HasMaxLength(200);
        b.Property(x => x.Codigo).HasMaxLength(40);
        b.Property(x => x.Color).HasMaxLength(9);
        b.Property(x => x.ResenaGoogleUrl).HasMaxLength(400);
        b.HasIndex(x => x.Codigo).IsUnique();

        // Uno a uno: cada comercio tiene un programa de sellos; la clave foránea está en el programa.
        b.HasOne(x => x.Programa).WithOne(p => p.Comercio).HasForeignKey<ProgramaSellos>(p => p.ComercioId);
    }
}

public class ProgramaSellosConfiguracion : IEntityTypeConfiguration<ProgramaSellos>
{
    public void Configure(EntityTypeBuilder<ProgramaSellos> b)
    {
        b.Property(x => x.Premio).HasMaxLength(120);
        b.Property(x => x.CostoPremio).HasPrecision(12, 2);
        b.HasIndex(x => x.ComercioId).IsUnique();
    }
}

public class ClienteConfiguracion : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        b.Property(x => x.Nombre).HasMaxLength(120);
        b.Property(x => x.Dni).HasMaxLength(20);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.CodigoQr).HasMaxLength(40);
        b.HasIndex(x => x.Dni).IsUnique();
        b.HasIndex(x => x.Telefono).IsUnique();
        b.HasIndex(x => x.CodigoQr).IsUnique();
    }
}

public class TarjetaConfiguracion : IEntityTypeConfiguration<Tarjeta>
{
    public void Configure(EntityTypeBuilder<Tarjeta> b)
    {
        b.HasIndex(x => new { x.ClienteId, x.ComercioId }).IsUnique();
        b.HasOne(x => x.Cliente).WithMany(c => c.Tarjetas).HasForeignKey(x => x.ClienteId);
        b.HasOne(x => x.Comercio).WithMany(c => c.Tarjetas).HasForeignKey(x => x.ComercioId);
        b.HasMany(x => x.Movimientos).WithOne(m => m.Tarjeta).HasForeignKey(m => m.TarjetaId);
        b.HasMany(x => x.Canjes).WithOne(c => c.Tarjeta).HasForeignKey(c => c.TarjetaId);
        b.Ignore(x => x.Nivel);
    }
}

public class MovimientoConfiguracion : IEntityTypeConfiguration<Movimiento>
{
    public void Configure(EntityTypeBuilder<Movimiento> b) => b.Property(x => x.CargadoPor).HasMaxLength(80);
}

public class CanjeConfiguracion : IEntityTypeConfiguration<Canje>
{
    public void Configure(EntityTypeBuilder<Canje> b)
    {
        b.Property(x => x.Codigo).HasMaxLength(12);
        b.Property(x => x.Premio).HasMaxLength(120);
        b.Property(x => x.EntregadoPor).HasMaxLength(120);
        b.HasIndex(x => x.Codigo).IsUnique();
    }
}

public class AvisoConfiguracion : IEntityTypeConfiguration<Aviso>
{
    public void Configure(EntityTypeBuilder<Aviso> b) => b.Property(x => x.Mensaje).HasMaxLength(300);
}

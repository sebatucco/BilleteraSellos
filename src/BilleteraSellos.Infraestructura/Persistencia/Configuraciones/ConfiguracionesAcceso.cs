using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BilleteraSellos.Infraestructura.Persistencia.Configuraciones;

public class UsuarioConfiguracion : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.Property(x => x.Nombre).HasMaxLength(120);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.PasswordHash).HasMaxLength(200);
        b.HasIndex(x => x.Email).IsUnique();
        b.HasOne(x => x.Comercio).WithMany().HasForeignKey(x => x.ComercioId);
    }
}

public class SesionConfiguracion : IEntityTypeConfiguration<Sesion>
{
    public void Configure(EntityTypeBuilder<Sesion> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.HasIndex(x => x.TokenHash).IsUnique();
    }
}

public class CodigoVerificacionConfiguracion : IEntityTypeConfiguration<CodigoVerificacion>
{
    public void Configure(EntityTypeBuilder<CodigoVerificacion> b)
    {
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.CodigoHash).HasMaxLength(64);
        b.HasIndex(x => x.Telefono);
        b.Ignore(x => x.AgotoIntentos);
    }
}

public class TokenRestablecimientoConfiguracion : IEntityTypeConfiguration<TokenRestablecimiento>
{
    public void Configure(EntityTypeBuilder<TokenRestablecimiento> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
    }
}

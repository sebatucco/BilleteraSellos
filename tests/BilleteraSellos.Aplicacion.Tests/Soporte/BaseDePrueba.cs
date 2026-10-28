using Microsoft.Data.Sqlite;

namespace BilleteraSellos.Aplicacion.Tests.Soporte;

/// <summary>
/// Cada test arranca con una base SQLite nueva en memoria, el AppDbContext real (mismas configuraciones que producción)
/// y dobles de prueba para la hora, las contraseñas, los SMS, los emails y los avisos.
/// </summary>
public abstract class BaseDePrueba : IDisposable
{
    /// <summary>15/10/2026 15:00 UTC = 12:00 en Argentina.</summary>
    protected static readonly DateTime Inicio = new(2026, 10, 15, 15, 0, 0, DateTimeKind.Utc);

    readonly SqliteConnection _conexion;
    readonly List<AppDbContext> _contextos = new();

    protected AppDbContext Db { get; }
    protected RelojFalso Reloj { get; } = new(Inicio);
    protected HasherFalso Hasher { get; } = new();
    protected EnviadorCodigosFalso Sms { get; } = new();
    protected EnviadorEmailFalso Correo { get; } = new();
    protected NotificadorFalso Notificador { get; } = new();
    protected ServicioSesiones Sesiones { get; }
    protected OpcionesApp Opciones { get; } = new("Billetera de Sellos", "http://localhost:5225");

    protected BaseDePrueba()
    {
        // La base en memoria vive mientras la conexión esté abierta.
        _conexion = new SqliteConnection("DataSource=:memory:");
        _conexion.Open();
        Db = NuevoContexto();
        Db.Database.EnsureCreated();
        Sesiones = new ServicioSesiones(Db, Reloj);
    }

    /// <summary>Otro contexto sobre la misma base, para comprobar lo que realmente quedó guardado (sin el caché del contexto del test).</summary>
    protected AppDbContext NuevoContexto()
    {
        var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexion).Options);
        _contextos.Add(ctx);
        return ctx;
    }

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        _conexion.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---------- Datos de prueba ----------

    protected async Task<Comercio> ComercioAsync(
        string nombre = "Café Tostado", string codigo = "cafe-tostado", int sellos = 10,
        int horasEntreSellos = 0, int diasParaRetirar = 7, string premio = "Café gratis")
    {
        var programa = ProgramaSellos.Nuevo(sellos, premio, 900);
        Assert.True(programa.Actualizar(sellos, premio, 900, diasParaRetirar, horasEntreSellos).Exito);
        var comercio = Comercio.Crear(nombre, "Cafetería", "Av. Siempreviva 742", codigo, null, programa);
        Db.Comercios.Add(comercio);
        await Db.SaveChangesAsync();
        return comercio;
    }

    protected async Task<Cliente> ClienteAsync(
        string nombre = "Lucía Fernández", string dni = "30111222", string telefono = "1154873320", DateOnly? nacimiento = null)
    {
        var cliente = Cliente.Registrar(nombre, dni, telefono, nacimiento, Reloj.HoyLocal).Valor;
        Db.Clientes.Add(cliente);
        await Db.SaveChangesAsync();
        return cliente;
    }

    protected async Task<Usuario> UsuarioAsync(
        Comercio comercio, string email = "cafe@demo.com", string contrasena = "Demo1234",
        RolUsuario rol = RolUsuario.Dueno, string nombre = "Carla (dueña)")
    {
        var u = Usuario.Crear(comercio.Id, nombre, email, Hasher.Hashear(contrasena), rol);
        Db.Usuarios.Add(u);
        await Db.SaveChangesAsync();
        return u;
    }

    /// <summary>Tarjeta con visitas cada <paramref name="cadaDias"/> días; la última hace <paramref name="ultimaHaceDias"/> días.</summary>
    protected async Task<Tarjeta> TarjetaConHistorialAsync(Cliente cliente, Comercio comercio, int visitas, int cadaDias, int ultimaHaceDias)
    {
        var ultima = Reloj.UtcAhora.AddDays(-ultimaHaceDias);
        var fechas = Enumerable.Range(0, visitas).Select(i => ultima.AddDays(-i * cadaDias));
        var t = Tarjeta.ImportarHistorial(cliente, comercio, fechas, comercio.Programa!.SellosNecesarios, "prueba");
        Db.Tarjetas.Add(t);
        await Db.SaveChangesAsync();
        return t;
    }

    protected static void AssertError(Resultado r, TipoError tipo, string? mensaje = null)
    {
        Assert.False(r.Exito, "Se esperaba un error y el resultado fue exitoso.");
        Assert.Equal(tipo, r.Error!.Tipo);
        if (mensaje is not null) Assert.Equal(mensaje, r.Error.Mensaje);
    }
}

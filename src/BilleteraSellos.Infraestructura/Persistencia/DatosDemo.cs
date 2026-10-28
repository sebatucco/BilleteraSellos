namespace BilleteraSellos.Infraestructura.Persistencia;

/// <summary>Carga comercios, usuarios y clientes de ejemplo la primera vez (solo en desarrollo).</summary>
public static class DatosDemo
{
    public const string ContrasenaDemo = "Demo1234";

    public static async Task CargarAsync(AppDbContext db, IHasherContrasenas hasher, CancellationToken ct = default)
    {
        if (await db.Comercios.AnyAsync(ct)) return;

        var cafe = NuevoComercio("Café Tostado", "Cafetería", "Av. Rivadavia 4120", "cafe-tostado", "#7A4B2A", 10, "Café con medialuna gratis", 900);
        var barberia = NuevoComercio("Barbería Norte", "Barbería", "Gorriti 3360", "barberia-norte", "#1F5F73", 6, "Corte gratis", 6000);
        var panaderia = NuevoComercio("Panadería La Espiga", "Panadería", "Corrientes 5502", "la-espiga", "#A2541C", 8, "20% en tu compra", 1500);
        var verduleria = NuevoComercio("Verdulería Doña Rosa", "Verdulería", "Lavalleja 812", "dona-rosa", "#2E7A3E", 10, "Bolsa de fruta de estación", 2500);
        db.Comercios.AddRange(cafe, barberia, panaderia, verduleria);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var lucia = NuevoCliente("Lucía Fernández", "30111222", "1154873320", new DateOnly(1994, 3, 8), hoy);
        var martin = NuevoCliente("Martín Gómez", "28444555", "1160001111", hoy.AddDays(2).AddYears(-31), hoy);
        var sofia = NuevoCliente("Sofía López", "33777888", "1162223333", hoy.AddYears(-27), hoy);
        var diego = NuevoCliente("Diego Ruiz", "25999000", "1164445555", new DateOnly(1988, 11, 20), hoy);
        db.Clientes.AddRange(lucia, martin, sofia, diego);

        // Usuarios del panel. Contraseña de todos: Demo1234 (solo existen en desarrollo).
        var hash = hasher.Hashear(ContrasenaDemo);
        db.Usuarios.AddRange(
            Usuario.Crear(cafe, "Carla (dueña)", "cafe@demo.com", hash, RolUsuario.Dueno),
            Usuario.Crear(cafe, "Juan (caja)", "caja@cafe.demo", hash, RolUsuario.Cajero),
            Usuario.Crear(barberia, "Nico (dueño)", "barberia@demo.com", hash, RolUsuario.Dueno),
            Usuario.Crear(panaderia, "Marta (dueña)", "panaderia@demo.com", hash, RolUsuario.Dueno),
            Usuario.Crear(verduleria, "Rosa (dueña)", "verduleria@demo.com", hash, RolUsuario.Dueno));

        // Historias de visitas: cada cuántos días venía y hace cuántos días vino por última vez.
        AgregarHistoria(db, lucia, cafe, visitas: 34, cadaDias: 3, ultimaHaceDias: 1);
        AgregarHistoria(db, lucia, barberia, visitas: 11, cadaDias: 21, ultimaHaceDias: 10);
        AgregarHistoria(db, lucia, panaderia, visitas: 4, cadaDias: 6, ultimaHaceDias: 2);
        AgregarHistoria(db, martin, cafe, visitas: 9, cadaDias: 5, ultimaHaceDias: 24);
        AgregarHistoria(db, sofia, cafe, visitas: 6, cadaDias: 12, ultimaHaceDias: 41);
        AgregarHistoria(db, diego, barberia, visitas: 7, cadaDias: 30, ultimaHaceDias: 62);

        await db.SaveChangesAsync(ct);
    }

    static Comercio NuevoComercio(string nombre, string rubro, string dir, string codigo, string color, int sellos, string premio, decimal costo) =>
        Comercio.Crear(nombre, rubro, dir, codigo, color, ProgramaSellos.Nuevo(sellos, premio, costo),
            "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString($"{nombre} {dir}"));

    static Cliente NuevoCliente(string nombre, string dni, string tel, DateOnly nacimiento, DateOnly hoy) =>
        Cliente.Registrar(nombre, dni, tel, nacimiento, hoy).Valor;

    static void AgregarHistoria(AppDbContext db, Cliente cliente, Comercio comercio, int visitas, int cadaDias, int ultimaHaceDias)
    {
        var ultima = DateTime.UtcNow.AddDays(-ultimaHaceDias);
        var fechas = Enumerable.Range(0, visitas).Select(i => ultima.AddDays(-i * cadaDias));
        db.Tarjetas.Add(Tarjeta.ImportarHistorial(cliente, comercio, fechas, comercio.Programa!.SellosNecesarios, "demo"));
    }
}

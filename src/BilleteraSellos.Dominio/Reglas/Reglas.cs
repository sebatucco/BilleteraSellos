using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace BilleteraSellos.Dominio.Reglas;

public enum Nivel { Bronce, Plata, Oro }

/// <summary>Niveles por sellos acumulados en un mismo comercio.</summary>
public static class Niveles
{
    public const int Plata = 10;
    public const int Oro = 30;

    public static Nivel Calcular(int sellosTotales) =>
        sellosTotales >= Oro ? Nivel.Oro : sellosTotales >= Plata ? Nivel.Plata : Nivel.Bronce;

    /// <summary>Sellos que faltan para el próximo nivel; null si ya es Oro.</summary>
    public static int? FaltanParaSiguiente(int sellosTotales) =>
        sellosTotales >= Oro ? null : (sellosTotales >= Plata ? Oro : Plata) - sellosTotales;
}

public enum EstadoCliente { AlDia, EnRiesgo, Critico, SinHistorial }

public record Ritmo(int DiasSinVenir, double? CadaCuantosDias, double? Relacion, EstadoCliente Estado);

/// <summary>
/// Detecta clientes que se enfrían comparando con su propio ritmo de visitas,
/// no con un número fijo de días: el doble de su ritmo es riesgo, el triple es crítico.
/// </summary>
public static class Enfriamiento
{
    public const double UmbralRiesgo = 2.0;
    public const double UmbralCritico = 3.0;

    public static Ritmo Evaluar(IReadOnlyList<DateTime> visitas, DateTime ahora)
    {
        if (visitas.Count == 0) return new Ritmo(0, null, null, EstadoCliente.SinHistorial);

        var ordenadas = visitas.OrderBy(v => v).ToList();
        var diasSinVenir = (int)Math.Floor((ahora - ordenadas[^1]).TotalDays);
        if (ordenadas.Count < 2) return new Ritmo(diasSinVenir, null, null, EstadoCliente.SinHistorial);

        // Promedio entre visitas, con un mínimo de 1 día para no exagerar a quien viene dos veces el mismo día.
        var cada = Math.Max(1.0, (ordenadas[^1] - ordenadas[0]).TotalDays / (ordenadas.Count - 1));
        var relacion = diasSinVenir / cada;
        var estado = relacion >= UmbralCritico ? EstadoCliente.Critico
                   : relacion >= UmbralRiesgo ? EstadoCliente.EnRiesgo
                   : EstadoCliente.AlDia;
        return new Ritmo(diasSinVenir, Math.Round(cada, 1), Math.Round(relacion, 2), estado);
    }

    public static bool EstaEnfriandose(Ritmo r) => r.Estado is EstadoCliente.EnRiesgo or EstadoCliente.Critico;
}

public record CostoEstimado(double PremiosPorMes, decimal CostoMensual, decimal VentasMensuales, double PorcentajeDeVentas, decimal CostoPorCompra, string Semaforo);

/// <summary>Cuánto le cuesta al comercio su tarjeta de sellos antes de publicarla.</summary>
public static class CalculadoraCosto
{
    public static CostoEstimado Calcular(int sellosNecesarios, decimal costoPremio, decimal ticketPromedio, int comprasConSelloPorMes)
    {
        if (sellosNecesarios < 1) throw new ArgumentOutOfRangeException(nameof(sellosNecesarios), "Tiene que ser al menos 1 sello.");
        var premios = (double)comprasConSelloPorMes / sellosNecesarios;
        var costo = Math.Round(costoPremio * comprasConSelloPorMes / sellosNecesarios, 0);
        var ventas = ticketPromedio * comprasConSelloPorMes;
        var pct = ventas == 0 ? 0 : (double)(costo / ventas * 100);
        var semaforo = pct <= 3 ? "Sano" : pct <= 6 ? "Revisalo" : "Caro";
        return new CostoEstimado(Math.Round(premios, 1), costo, ventas, Math.Round(pct, 1), Math.Round(costoPremio / sellosNecesarios, 0), semaforo);
    }
}

public static class Codigos
{
    // Sin 0/O ni 1/I/L para que se pueda dictar y tipear en la caja sin confusiones.
    const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string Generar(int largo = 6) =>
        string.Create(largo, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        });

    /// <summary>Deja solo dígitos: "30.111.222" o "+54 11 5487-3320" se buscan igual.</summary>
    public static string SoloDigitos(string? texto) => new((texto ?? "").Where(char.IsDigit).ToArray());
}

public static class Telefonos
{
    /// <summary>
    /// Teléfono argentino normalizado a los 10 dígitos de característica + número, como se marca sin el 0 ni el 15:
    /// "+54 9 11 5487-3320", "011 15 5487-3320" y "11 5487 3320" quedan todos "1154873320".
    /// </summary>
    public static string Normalizar(string? telefono)
    {
        var d = Codigos.SoloDigitos(telefono);
        if (d.StartsWith("549")) d = d[3..];
        else if (d.StartsWith("54")) d = d[2..];
        d = d.TrimStart('0');
        // Con el 15 de celular quedan 12 dígitos: se busca el 15 después de una característica de 2, 3 o 4 dígitos.
        if (d.Length == 12)
            foreach (var largoArea in new[] { 2, 3, 4 })
                if (d.Substring(largoArea, 2) == "15") { d = d[..largoArea] + d[(largoArea + 2)..]; break; }
        return d;
    }

    /// <summary>Un teléfono normalizado es válido si tiene entre 8 y 13 dígitos.</summary>
    public static bool EsValido(string normalizado) => normalizado.Length is >= 8 and <= 13;
}

public static class Emails
{
    /// <summary>Email en minúsculas y sin espacios, o null si no es válido.</summary>
    public static string? Normalizar(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200) return null;
        try { return new MailAddress(email.Trim()).Address.ToLowerInvariant(); }
        catch (FormatException) { return null; }
    }
}

public static class PoliticaContrasenas
{
    /// <summary>Devuelve el motivo si la contraseña es débil, o null si sirve.</summary>
    public static string? Validar(string? contrasena)
    {
        if (string.IsNullOrEmpty(contrasena) || contrasena.Length < 8) return "La contraseña tiene que tener al menos 8 caracteres.";
        if (!contrasena.Any(char.IsLetter) || !contrasena.Any(char.IsDigit)) return "La contraseña tiene que tener letras y números.";
        return null;
    }
}

public static class Cumpleanos
{
    public static bool EsSuCumpleanos(DateOnly? nacimiento, DateOnly hoy)
    {
        if (nacimiento is not { } n) return false;
        // Los nacidos el 29/2 festejan el 28/2 en años no bisiestos.
        if (n.Month == 2 && n.Day == 29 && !DateTime.IsLeapYear(hoy.Year)) return hoy.Month == 2 && hoy.Day == 28;
        return n.Month == hoy.Month && n.Day == hoy.Day;
    }

    /// <summary>Fecha del próximo cumpleaños desde hoy (hoy incluido).</summary>
    public static DateOnly Proximo(DateOnly nacimiento, DateOnly hoy)
    {
        DateOnly EnAnio(int anio) => nacimiento.Month == 2 && nacimiento.Day == 29 && !DateTime.IsLeapYear(anio)
            ? new DateOnly(anio, 2, 28) : new DateOnly(anio, nacimiento.Month, nacimiento.Day);
        var esteAnio = EnAnio(hoy.Year);
        return esteAnio >= hoy ? esteAnio : EnAnio(hoy.Year + 1);
    }
}

public static class CodigosComercio
{
    /// <summary>Código base del QR del mostrador a partir del nombre: "Café Tostado" → "cafe-tostado".</summary>
    public static string Base(string nombre)
    {
        var sb = new StringBuilder();
        foreach (var ch in (nombre ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }
        var codigo = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (codigo.Length == 0) codigo = "comercio";
        if (codigo.Length > 30) codigo = codigo[..30].TrimEnd('-');
        return codigo;
    }
}

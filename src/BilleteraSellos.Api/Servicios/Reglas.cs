using System.Security.Cryptography;

namespace BilleteraSellos.Api.Servicios;

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
    public static string SoloDigitos(string texto) => new(texto.Where(char.IsDigit).ToArray());
}

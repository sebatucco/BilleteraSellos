namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// Lo común a sumar sellos desde la caja (por DNI o QR del cliente) y desde el QR de la compra:
/// suma en la tarjeta y, si con esta compra se completa, le avisa al cliente.
/// </summary>
internal static class Sellado
{
    /// <summary>Devuelve true si con esta compra la tarjeta pasó a estar completa. No guarda: eso lo hace el handler.</summary>
    public static async Task<Resultado<bool>> SumarAsync(IAppDbContext db, INotificador notificador, Tarjeta tarjeta, ProgramaSellos programa,
        Comercio comercio, Cliente cliente, int cantidad, decimal? monto, DateTime ahora, string? cargadoPor, CancellationToken ct)
    {
        var estabaCompleta = tarjeta.EstaCompleta(programa);
        var r = tarjeta.SumarSellos(programa, cantidad, monto, ahora, cargadoPor);
        if (!r.Exito) return r.Error!;

        // Se avisa solo cuando la compra cruza el umbral, no en cada sello que sobra.
        var seCompleto = !estabaCompleta && tarjeta.EstaCompleta(programa);
        if (seCompleto)
        {
            var mensaje = $"¡Completaste tu tarjeta en {comercio.Nombre}! Ya podés canjear: {programa.Premio}.";
            await notificador.EnviarAsync(cliente, comercio, TipoAviso.TarjetaCompleta, mensaje, ct);
            db.Avisos.Add(Aviso.Crear(cliente.Id, comercio.Id, TipoAviso.TarjetaCompleta, mensaje, ahora));
        }
        return seCompleto;
    }

    /// <summary>"Lucía Fernández" → "Lucía F.": suficiente para que el cajero confirme, sin mostrar el nombre completo.</summary>
    public static string NombreCorto(string nombre)
    {
        var partes = nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length switch
        {
            0 => "",
            1 => partes[0],
            _ => $"{partes[0]} {char.ToUpperInvariant(partes[^1][0])}.",
        };
    }
}

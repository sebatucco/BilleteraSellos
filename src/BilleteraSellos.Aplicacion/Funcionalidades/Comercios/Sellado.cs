namespace BilleteraSellos.Aplicacion.Funcionalidades.Comercios;

/// <summary>
/// Lo común a sumar sellos desde la caja (por DNI o QR del cliente) y desde el QR de la compra:
/// suma en la tarjeta, le avisa al cliente si con esta compra se completa y, si es la visita elegida, le pide la reseña de Google.
/// </summary>
internal static class Sellado
{
    /// <summary>Devuelve true si con esta compra la tarjeta pasó a estar completa. No guarda: eso lo hace el handler.</summary>
    public static async Task<Resultado<bool>> SumarAsync(IAppDbContext db, INotificador notificador, Tarjeta tarjeta, ProgramaSellos programa,
        Comercio comercio, Cliente cliente, int cantidad, decimal? monto, DateTime ahora, string? cargadoPor, CancellationToken ct)
    {
        var estabaCompleta = tarjeta.EstaCompleta(programa);
        // Visitas anteriores (compras con sello), para saber si esta es la visita en la que se pide la reseña.
        var visitasPrevias = tarjeta.Id == 0 ? 0
            : await db.Movimientos.CountAsync(m => m.TarjetaId == tarjeta.Id && m.Tipo == TipoMovimiento.Sello, ct);
        var r = tarjeta.SumarSellos(programa, cantidad, monto, ahora, cargadoPor);
        if (!r.Exito) return r.Error!;

        // Pedido de reseña en Google: una sola vez, en la visita que eligió el comercio. Nunca da sellos ni premios.
        if (tarjeta.CorrespondePedirResena(visitasPrevias + 1, programa, comercio.ResenaGoogleUrl))
        {
            var pedido = $"¿Te gustó {comercio.Nombre}? Contanos en Google: {comercio.ResenaGoogleUrl}";
            await notificador.EnviarAsync(cliente, comercio, TipoAviso.Resena, pedido, ct);
            db.Avisos.Add(Aviso.Crear(cliente.Id, comercio.Id, TipoAviso.Resena, pedido, ahora));
            tarjeta.MarcarResenaPedida(ahora);
        }

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

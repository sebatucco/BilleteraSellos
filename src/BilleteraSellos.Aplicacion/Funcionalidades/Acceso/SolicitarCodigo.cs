using System.Security.Cryptography;

namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>
/// Cliente, paso 1: manda un código de 6 números al teléfono. Devuelve el código para que la Api
/// pueda mostrarlo solo en desarrollo (cuando no hay SMS real).
/// </summary>
public sealed record SolicitarCodigo(string? Telefono) : ICommand<Resultado<CodigoSolicitado>>;

public sealed record CodigoSolicitado(string Codigo, int VenceEnMinutos);

public sealed class SolicitarCodigoHandler(IAppDbContext db, IEnviadorCodigos enviador, IReloj reloj)
    : ICommandHandler<SolicitarCodigo, Resultado<CodigoSolicitado>>
{
    public async Task<Resultado<CodigoSolicitado>> Handle(SolicitarCodigo c, CancellationToken ct)
    {
        var tel = Telefonos.Normalizar(c.Telefono);
        if (!Telefonos.EsValido(tel)) return Error.Validacion("Escribí el teléfono con código de área, por ejemplo 11 5487 3320.");

        var ahora = reloj.UtcAhora;
        var hace1h = ahora.AddHours(-1);
        var haceUnMinuto = ahora.AddMinutes(-1);
        var recientes = await db.CodigosVerificacion.Where(x => x.Telefono == tel && x.CreadoEn >= hace1h).ToListAsync(ct);
        if (recientes.Any(x => x.CreadoEn >= haceUnMinuto)) return Error.DemasiadosIntentos("Ya te mandamos un código. Esperá un minuto para pedir otro.");
        if (recientes.Count >= CodigoVerificacion.MaxPorHora) return Error.DemasiadosIntentos("Pediste demasiados códigos. Probá de nuevo en una hora.");

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        // Primero se envía: si el SMS falla, no queda guardado un código que nadie recibió
        // y los códigos anteriores siguen sirviendo.
        try { await enviador.EnviarAsync(tel, codigo, ct); }
        catch (EnvioFallidoException ex) { return Error.ServicioExterno(ex.Message); }

        foreach (var previo in recientes) previo.Anular(ahora);
        db.CodigosVerificacion.Add(CodigoVerificacion.Crear(tel, Tokens.Hash(tel + ":" + codigo), ahora));
        await db.SaveChangesAsync(ct);
        return new CodigoSolicitado(codigo, (int)CodigoVerificacion.Vigencia.TotalMinutes);
    }
}

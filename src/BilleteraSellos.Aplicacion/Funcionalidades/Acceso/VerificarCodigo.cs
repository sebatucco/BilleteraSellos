namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Cliente, paso 2: verifica el código. Entra si ya tenía cuenta; si no, recibe un token para registrarse.</summary>
public sealed record VerificarCodigo(string? Telefono, string? Codigo) : ICommand<Resultado<ResultadoVerificar>>;

/// <summary>Si el teléfono ya tiene cuenta, viene la sesión del cliente; si no, un token para terminar el registro.</summary>
public sealed record ResultadoVerificar(bool RequiereRegistro, string Token, DateTime VenceEn, int? ClienteId, string? Nombre);

public sealed class VerificarCodigoHandler(IAppDbContext db, ServicioSesiones sesiones, IReloj reloj)
    : ICommandHandler<VerificarCodigo, Resultado<ResultadoVerificar>>
{
    public async Task<Resultado<ResultadoVerificar>> Handle(VerificarCodigo c, CancellationToken ct)
    {
        var tel = Telefonos.Normalizar(c.Telefono);
        var ahora = reloj.UtcAhora;
        var vigentes = await db.CodigosVerificacion
            .Where(x => x.Telefono == tel && x.UsadoEn == null && x.VenceEn > ahora).ToListAsync(ct);
        var actual = vigentes.OrderByDescending(x => x.CreadoEn).FirstOrDefault();
        if (actual is null) return Error.NoAutorizado("El código venció o no existe. Pedí uno nuevo.");
        if (actual.AgotoIntentos) return Error.DemasiadosIntentos("Demasiados intentos. Pedí un código nuevo.");

        actual.RegistrarIntento();
        var correcto = Tokens.HashesIguales(actual.CodigoHash, Tokens.Hash(tel + ":" + Codigos.SoloDigitos(c.Codigo)));
        if (correcto) actual.Usar(ahora);
        await db.SaveChangesAsync(ct);
        if (!correcto)
            return actual.AgotoIntentos
                ? Error.DemasiadosIntentos("Demasiados intentos. Pedí un código nuevo.")
                : Error.NoAutorizado("El código no es correcto.");

        var cliente = await db.Clientes.FirstOrDefaultAsync(x => x.Telefono == tel, ct);
        if (cliente is null)
        {
            var (tokenRegistro, venceRegistro) = await sesiones.AbrirAsync(TipoSesion.Registro, telefono: tel, ct: ct);
            return new ResultadoVerificar(true, tokenRegistro, venceRegistro, null, null);
        }
        var (token, vence) = await sesiones.AbrirAsync(TipoSesion.Cliente, clienteId: cliente.Id, ct: ct);
        return new ResultadoVerificar(false, token, vence, cliente.Id, cliente.Nombre);
    }
}

namespace BilleteraSellos.Aplicacion.Funcionalidades.Acceso;

/// <summary>Cierra la sesión del token que hizo el pedido.</summary>
public sealed record CerrarSesion(int SesionId) : ICommand<Resultado>;

public sealed class CerrarSesionHandler(ServicioSesiones sesiones) : ICommandHandler<CerrarSesion, Resultado>
{
    public async Task<Resultado> Handle(CerrarSesion c, CancellationToken ct)
    {
        await sesiones.CerrarAsync(c.SesionId, ct);
        return Resultado.Ok();
    }
}

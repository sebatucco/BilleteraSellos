namespace BilleteraSellos.Api.Endpoints;

/// <summary>Convierte el resultado de un caso de uso en una respuesta HTTP. Los errores siempre son { "error": "..." }.</summary>
public static class Respuestas
{
    public static IResult ARespuesta<T>(this Resultado<T> r, Func<T, IResult>? siSaleBien = null) =>
        r.Exito ? (siSaleBien?.Invoke(r.Valor) ?? Results.Ok(r.Valor)) : ErrorHttp(r.Error!);

    public static IResult ARespuesta(this Resultado r, Func<IResult> siSaleBien) =>
        r.Exito ? siSaleBien() : ErrorHttp(r.Error!);

    public static IResult ErrorHttp(Error e) => Results.Json(new { error = e.Mensaje }, statusCode: e.Tipo switch
    {
        TipoError.Validacion => StatusCodes.Status400BadRequest,
        TipoError.NoEncontrado => StatusCodes.Status404NotFound,
        TipoError.Conflicto => StatusCodes.Status409Conflict,
        TipoError.NoAutorizado => StatusCodes.Status401Unauthorized,
        TipoError.Prohibido => StatusCodes.Status403Forbidden,
        TipoError.DemasiadosIntentos => StatusCodes.Status429TooManyRequests,
        TipoError.ServicioExterno => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError,
    });
}

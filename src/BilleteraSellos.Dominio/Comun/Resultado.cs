namespace BilleteraSellos.Dominio.Comun;

/// <summary>Tipos de error del negocio. La Api los traduce a códigos HTTP.</summary>
public enum TipoError { Validacion, NoEncontrado, Conflicto, NoAutorizado, Prohibido, DemasiadosIntentos, ServicioExterno }

/// <summary>Un error esperado del negocio, con un mensaje apto para mostrarle al usuario.</summary>
public sealed record Error(TipoError Tipo, string Mensaje)
{
    public static Error Validacion(string mensaje) => new(TipoError.Validacion, mensaje);
    public static Error NoEncontrado(string mensaje) => new(TipoError.NoEncontrado, mensaje);
    public static Error Conflicto(string mensaje) => new(TipoError.Conflicto, mensaje);
    public static Error NoAutorizado(string mensaje) => new(TipoError.NoAutorizado, mensaje);
    public static Error Prohibido(string mensaje) => new(TipoError.Prohibido, mensaje);
    public static Error DemasiadosIntentos(string mensaje) => new(TipoError.DemasiadosIntentos, mensaje);
    public static Error ServicioExterno(string mensaje) => new(TipoError.ServicioExterno, mensaje);
}

/// <summary>Resultado de una operación que puede fallar por una regla del negocio, sin usar excepciones.</summary>
public class Resultado
{
    protected Resultado(Error? error) => Error = error;

    public Error? Error { get; }
    public bool Exito => Error is null;

    public static Resultado Ok() => new(null);
    public static Resultado Falla(Error error) => new(error);

    public static implicit operator Resultado(Error error) => Falla(error);
}

/// <summary>Resultado con un valor cuando sale bien.</summary>
public sealed class Resultado<T> : Resultado
{
    readonly T? _valor;

    Resultado(T valor) : base(null) => _valor = valor;
    Resultado(Error error) : base(error) { }

    /// <summary>El valor. Solo se puede leer si <see cref="Resultado.Exito"/> es true.</summary>
    public T Valor => Exito ? _valor! : throw new InvalidOperationException($"El resultado falló: {Error!.Mensaje}");

    public static Resultado<T> Ok(T valor) => new(valor);
    public static new Resultado<T> Falla(Error error) => new(error);

    public static implicit operator Resultado<T>(T valor) => Ok(valor);
    public static implicit operator Resultado<T>(Error error) => Falla(error);
}

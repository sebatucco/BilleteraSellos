using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BilleteraSellos.Aplicacion.Mensajeria;

/// <summary>Un pedido que cambia el estado del sistema.</summary>
public interface ICommand<TResultado> { }

/// <summary>Un pedido que solo lee.</summary>
public interface IQuery<TResultado> { }

public interface ICommandHandler<in TComando, TResultado> where TComando : ICommand<TResultado>
{
    Task<TResultado> Handle(TComando comando, CancellationToken ct);
}

public interface IQueryHandler<in TConsulta, TResultado> where TConsulta : IQuery<TResultado>
{
    Task<TResultado> Handle(TConsulta consulta, CancellationToken ct);
}

public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de la capa de aplicación y todos los handlers del ensamblado,
    /// por su interfaz y por su clase concreta (los endpoints inyectan la clase concreta).
    /// No hace falta MediatR: son handlers comunes resueltos por inyección de dependencias.
    /// </summary>
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios)
    {
        servicios.AddScoped<ServicioSesiones>();

        var abiertas = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        var handlers = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });
        foreach (var tipo in handlers)
        {
            var interfaces = tipo.GetInterfaces()
                .Where(i => i.IsGenericType && abiertas.Contains(i.GetGenericTypeDefinition()))
                .ToList();
            if (interfaces.Count == 0) continue;
            servicios.TryAddScoped(tipo);
            foreach (var i in interfaces) servicios.AddScoped(i, sp => sp.GetRequiredService(tipo));
        }
        return servicios;
    }
}

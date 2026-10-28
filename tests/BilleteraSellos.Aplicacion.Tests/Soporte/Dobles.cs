namespace BilleteraSellos.Aplicacion.Tests.Soporte;

/// <summary>Reloj que se mueve solo cuando el test lo pide. La hora local es la de Argentina (UTC-3).</summary>
public sealed class RelojFalso(DateTime inicioUtc) : IReloj
{
    public DateTime UtcAhora { get; set; } = inicioUtc;
    public DateTime AhoraLocal => UtcAhora.AddHours(-3);
    public DateOnly HoyLocal => DateOnly.FromDateTime(AhoraLocal);

    public void Avanzar(TimeSpan tiempo) => UtcAhora = UtcAhora.Add(tiempo);
}

/// <summary>Hasher instantáneo para los tests (el real, PBKDF2, tarda a propósito).</summary>
public sealed class HasherFalso : IHasherContrasenas
{
    public string Hashear(string contrasena) => "hash:" + contrasena;
    public bool Verificar(string contrasena, string hashGuardado) => hashGuardado == "hash:" + contrasena;
}

/// <summary>Guarda los códigos "enviados" para que el test los lea. Con <see cref="Fallar"/> simula que el SMS no salió.</summary>
public sealed class EnviadorCodigosFalso : IEnviadorCodigos
{
    public List<(string Telefono, string Codigo)> Enviados { get; } = new();
    public bool Fallar { get; set; }

    public Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default)
    {
        if (Fallar) throw new EnvioFallidoException("No pudimos enviar el código. Probá de nuevo en unos minutos.");
        Enviados.Add((telefono, codigo));
        return Task.CompletedTask;
    }

    public string UltimoCodigo => Enviados[^1].Codigo;
}

public sealed class EnviadorEmailFalso : IEnviadorEmail
{
    public List<(string Para, string Asunto, string Cuerpo)> Enviados { get; } = new();
    public bool Fallar { get; set; }

    public Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default)
    {
        if (Fallar) throw new EnvioFallidoException("No pudimos enviar el email. Probá de nuevo en unos minutos.");
        Enviados.Add((para, asunto, cuerpo));
        return Task.CompletedTask;
    }

    /// <summary>El token que viene en el link "#restablecer=..." del último email.</summary>
    public string TokenDelUltimoLink()
    {
        const string marca = "#restablecer=";
        var cuerpo = Enviados[^1].Cuerpo;
        var desde = cuerpo.IndexOf(marca, StringComparison.Ordinal) + marca.Length;
        var hasta = cuerpo.IndexOfAny(new[] { '\n', ' ', '\r' }, desde);
        return hasta < 0 ? cuerpo[desde..] : cuerpo[desde..hasta];
    }
}

public sealed class NotificadorFalso : INotificador
{
    public List<(int ClienteId, int ComercioId, TipoAviso Tipo, string Mensaje)> Enviados { get; } = new();

    public Task EnviarAsync(Cliente cliente, Comercio comercio, TipoAviso tipo, string mensaje, CancellationToken ct = default)
    {
        Enviados.Add((cliente.Id, comercio.Id, tipo, mensaje));
        return Task.CompletedTask;
    }
}

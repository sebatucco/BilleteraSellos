using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BilleteraSellos.Infraestructura.Servicios;

/// <summary>Muestra el código en el log. Solo para desarrollo: en producción va por SMS.</summary>
public class EnviadorCodigosConsola(ILogger<EnviadorCodigosConsola> log) : IEnviadorCodigos
{
    public Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default)
    {
        log.LogWarning("CÓDIGO DE VERIFICACIÓN para {Telefono}: {Codigo} (solo desarrollo: en producción va por SMS)", telefono, codigo);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Envía el código de verificación por Twilio (SMS o WhatsApp) usando su API REST.
/// Configuración: Sms:Twilio:AccountSid, AuthToken, From, Canal ("sms" o "whatsapp") y UrlBase (para pruebas).
/// </summary>
public class EnviadorCodigosTwilio(HttpClient http, IConfiguration config, ILogger<EnviadorCodigosTwilio> log) : IEnviadorCodigos
{
    public async Task EnviarAsync(string telefono, string codigo, CancellationToken ct = default)
    {
        var sid = config["Sms:Twilio:AccountSid"]!;
        var authToken = config["Sms:Twilio:AuthToken"]!;
        var desde = config["Sms:Twilio:From"]!;
        var whatsapp = string.Equals(config["Sms:Twilio:Canal"], "whatsapp", StringComparison.OrdinalIgnoreCase);
        var urlBase = (config["Sms:Twilio:UrlBase"] ?? "https://api.twilio.com").TrimEnd('/');
        var app = config["NombreApp"] ?? "Billetera de Sellos";

        var para = AE164(telefono);
        string Canal(string n) => whatsapp && !n.StartsWith("whatsapp:") ? "whatsapp:" + n : n;

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{urlBase}/2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = Canal(para),
                ["From"] = Canal(desde),
                ["Body"] = $"Tu código de {app} es {codigo}. Vence en {(int)CodigoVerificacion.Vigencia.TotalMinutes} minutos. No lo compartas con nadie.",
            }),
        };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{authToken}")));

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.SendAsync(pedido, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogError(ex, "No se pudo conectar con Twilio para enviar el código a {Telefono}", Ocultar(para));
            throw new EnvioFallidoException("No pudimos enviar el código. Probá de nuevo en unos minutos.", ex);
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode) return;
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            log.LogError("Twilio rechazó el envío a {Telefono}: {Estado} {Detalle}", Ocultar(para), (int)respuesta.StatusCode, DetalleTwilio(cuerpo));
            // 400 de Twilio suele ser un número inválido o no habilitado (por ejemplo, fuera del sandbox de WhatsApp).
            throw new EnvioFallidoException((int)respuesta.StatusCode == 400
                ? "No pudimos enviar el código a ese número. Revisá que esté bien escrito."
                : "No pudimos enviar el código. Probá de nuevo en unos minutos.");
        }
    }

    /// <summary>Número argentino normalizado (sin 54, 9 ni 0 adelante) a E.164 de celular: +549...</summary>
    public static string AE164(string normalizado)
    {
        var d = new string(normalizado.Where(char.IsDigit).ToArray()).TrimStart('0');
        return "+549" + d;
    }

    static string Ocultar(string tel) => tel.Length <= 4 ? "****" : new string('*', tel.Length - 4) + tel[^4..];

    static string DetalleTwilio(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            var r = doc.RootElement;
            var codigo = r.TryGetProperty("code", out var c) ? c.ToString() : "?";
            var mensaje = r.TryGetProperty("message", out var m) ? m.GetString() : "";
            return $"código {codigo}: {mensaje}";
        }
        catch (JsonException) { return cuerpo.Length > 300 ? cuerpo[..300] : cuerpo; }
    }
}

public class EnviadorEmailConsola(ILogger<EnviadorEmailConsola> log) : IEnviadorEmail
{
    public Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default)
    {
        log.LogWarning("EMAIL para {Para} — {Asunto}\n{Cuerpo}\n(solo desarrollo: en producción va por SMTP)", para, asunto, cuerpo);
        return Task.CompletedTask;
    }
}

/// <summary>Envía por SMTP. Configuración: Email:Smtp:Host, Puerto, Usuario, Contrasena, De, UsarSsl.</summary>
public class EnviadorEmailSmtp(IConfiguration config, ILogger<EnviadorEmailSmtp> log) : IEnviadorEmail
{
    public async Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default)
    {
        var s = config.GetSection("Email:Smtp");
        using var cliente = new SmtpClient(s["Host"], s.GetValue("Puerto", 587))
        {
            EnableSsl = s.GetValue("UsarSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15_000,
        };
        if (!string.IsNullOrEmpty(s["Usuario"])) cliente.Credentials = new System.Net.NetworkCredential(s["Usuario"], s["Contrasena"]);

        using var mensaje = new MailMessage(new MailAddress(s["De"]!, config["NombreApp"] ?? "Billetera de Sellos"), new MailAddress(para))
        {
            Subject = asunto, Body = cuerpo, IsBodyHtml = false, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8,
        };
        try
        {
            await cliente.SendMailAsync(mensaje, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or IOException)
        {
            log.LogError(ex, "No se pudo enviar el email \"{Asunto}\"", asunto);
            throw new EnvioFallidoException("No pudimos enviar el email. Probá de nuevo en unos minutos.", ex);
        }
    }
}

using System.Data.Common;

namespace BilleteraSellos.Infraestructura.Persistencia;

/// <summary>
/// Railway, Render y Heroku entregan la base como una URL (DATABASE_URL=postgres://usuario:clave@host:puerto/base).
/// Npgsql necesita el formato clave=valor, así que se convierte acá.
/// </summary>
public static class ConexionBase
{
    public static string DesdeUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
            throw new FormatException("DATABASE_URL tiene que tener la forma postgres://usuario:clave@host:puerto/base");

        var info = uri.UserInfo.Split(':', 2);
        var b = new DbConnectionStringBuilder
        {
            ["Host"] = uri.Host,
            ["Port"] = uri.Port > 0 ? uri.Port : 5432,
            ["Database"] = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            ["Username"] = Uri.UnescapeDataString(info[0]),
        };
        if (info.Length > 1) b["Password"] = Uri.UnescapeDataString(info[1]);

        // Respeta ?sslmode=require que agregan algunos proveedores (Render, Neon, Supabase).
        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = par.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                b["SSL Mode"] = Uri.UnescapeDataString(kv[1]) switch
                {
                    var m when m.Equals("require", StringComparison.OrdinalIgnoreCase) => "Require",
                    var m when m.Equals("disable", StringComparison.OrdinalIgnoreCase) => "Disable",
                    var m when m.Equals("prefer", StringComparison.OrdinalIgnoreCase) => "Prefer",
                    var m when m.Equals("verify-full", StringComparison.OrdinalIgnoreCase) => "VerifyFull",
                    var m when m.Equals("verify-ca", StringComparison.OrdinalIgnoreCase) => "VerifyCA",
                    var m => m,
                };
        }
        return b.ConnectionString;
    }
}

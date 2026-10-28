using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BilleteraSellos.Api.Endpoints;
using BilleteraSellos.Api.Tareas;
using BilleteraSellos.Aplicacion.Funcionalidades.Avisos;
using BilleteraSellos.Aplicacion.Mensajeria;
using BilleteraSellos.Infraestructura;
using BilleteraSellos.Infraestructura.Persistencia;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models; // swagger

// Composición de la app: acá se juntan las capas. La lógica del negocio está en Dominio y Aplicacion.
var builder = WebApplication.CreateBuilder(args);

// --- Publicación (hosting) ---
// El hosting indica en PORT el puerto en el que tiene que escuchar la app.
if (builder.Configuration["PORT"] is { Length: > 0 } puerto && int.TryParse(puerto, out var nroPuerto))
    builder.WebHost.UseUrls($"http://0.0.0.0:{nroPuerto}");
// Detrás del proxy del hosting, la IP real y el https vienen en X-Forwarded-For / X-Forwarded-Proto.
var confiarEnProxy = builder.Configuration.GetValue("Proxy:ConfiarEnEncabezados", false);
if (confiarEnProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // El proxy del hosting no tiene una IP fija conocida: se confía en el primer salto.
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}
// --- fin publicación ---

// Capas: casos de uso (handlers) e infraestructura (base de datos, SMS, email, reloj, contraseñas).
builder.Services.AddAplicacion();
builder.Services.AddInfraestructura(builder.Configuration);
if (builder.Configuration.GetValue("Avisos:Activos", true))
    builder.Services.AddHostedService<AvisosAutomaticos>();

// Login: tokens de sesión guardados (hasheados) en la base, revocables al instante.
builder.Services.AddAuthentication(SesionHandler.Esquema)
    .AddScheme<AuthenticationSchemeOptions, SesionHandler>(SesionHandler.Esquema, null);
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Politicas.Comercio, p => p.RequireClaim(Reclamos.Tipo, "Comercio"));
    o.AddPolicy(Politicas.Dueno, p => p.RequireClaim(Reclamos.Tipo, "Comercio").RequireRole("Dueno"));
    o.AddPolicy(Politicas.Cliente, p => p.RequireClaim(Reclamos.Tipo, "Cliente"));
    o.AddPolicy(Politicas.Registro, p => p.RequireClaim(Reclamos.Tipo, "Registro"));
});

// Límite de pedidos a login y códigos por IP, contra ataques de fuerza bruta.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, ct) => new ValueTask(ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Demasiados intentos. Esperá un minuto." }, ct));
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("Seguridad:PedidosLoginPorMinuto", 20), Window = TimeSpan.FromMinutes(1) }));
});

// CORS: en desarrollo cualquier origen; en producción solo los dominios configurados.
var origenes = builder.Configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (builder.Environment.IsDevelopment()) p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer(); // swagger
builder.Services.AddSwaggerGen(o => // swagger
{ // swagger
    // Botón "Authorize" en Swagger: pegá el token que devuelve el login. // swagger
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "Token del login" }); // swagger
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } }); // swagger
}); // swagger

var app = builder.Build();

// PostgreSQL: aplica migraciones. SQLite: crea la base. En desarrollo carga los datos de ejemplo.
await InicializadorBase.InicializarAsync(app.Services, app.Environment.IsDevelopment());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();   // swagger
    app.UseSwaggerUI(); // swagger

    // Para probar los avisos sin esperar a la hora programada.
    app.MapPost("/api/avisos/procesar", (ProcesarAvisosHandler h, CancellationToken ct) => h.Handle(new ProcesarAvisos(), ct)).WithTags("Desarrollo");
}

// --- Publicación (hosting) ---
if (confiarEnProxy) app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Seguridad:ForzarHttps", true))
{
    app.UseHsts();
    // /salud queda en http para el chequeo interno del hosting.
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/salud"), a => a.UseHttpsRedirection());
}
// Chequeo de salud para el hosting: responde 200 si la app y la base andan.
app.MapGet("/salud", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { estado = "ok" })
        : Results.Json(new { estado = "sin conexión a la base" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithTags("Desarrollo").ExcludeFromDescription();
// --- fin publicación ---

AvisarSiEnviosDeConsola(app);
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAccesoEndpoints();
app.MapEquipoEndpoints();
app.MapComercioEndpoints();
app.MapBilleteraEndpoints();

app.Run();

/// <summary>En producción avisa si los códigos o emails solo salen por consola (nadie los recibiría).</summary>
static void AvisarSiEnviosDeConsola(WebApplication app)
{
    if (app.Environment.IsDevelopment()) return;
    if ((app.Configuration["Sms:Proveedor"] ?? "Consola").Equals("Consola", StringComparison.OrdinalIgnoreCase))
        app.Logger.LogWarning("Sms:Proveedor es Consola: los clientes NO van a recibir el código. Configurá Twilio.");
    if ((app.Configuration["Email:Proveedor"] ?? "Consola").Equals("Consola", StringComparison.OrdinalIgnoreCase))
        app.Logger.LogWarning("Email:Proveedor es Consola: los emails de recuperar contraseña NO se envían. Configurá SMTP.");
}

public partial class Program { }

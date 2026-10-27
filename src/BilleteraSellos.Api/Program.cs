using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Endpoints;
using BilleteraSellos.Api.Seguridad;
using BilleteraSellos.Api.Servicios;
using BilleteraSellos.Api.Tareas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models; // swagger

var builder = WebApplication.CreateBuilder(args);

// Base de datos: SQLite para desarrollar en tu PC, PostgreSQL en el servidor.
var proveedor = builder.Configuration["BaseDeDatos:Proveedor"] ?? "Sqlite";
var conexion = builder.Configuration.GetConnectionString("Principal") ?? "Data Source=billetera.db";
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (proveedor.Equals("Postgres", StringComparison.OrdinalIgnoreCase)) o.UseNpgsql(conexion);
    else o.UseSqlite(conexion);
});

builder.Services.AddSingleton<RelojLocal>();
builder.Services.AddSingleton<INotificador, NotificadorConsola>();
builder.Services.AddScoped<ProcesadorAvisos>();
if (builder.Configuration.GetValue("Avisos:Activos", true))
    builder.Services.AddHostedService<AvisosAutomaticos>();

// Login: tokens de sesión guardados (hasheados) en la base, revocables al instante.
builder.Services.AddScoped<Sesiones>();
builder.Services.AddScoped<CodigosVerificacion>();
builder.AgregarEnvios(); // SMS (Consola/Twilio) y email (Consola/SMTP) según appsettings
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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Crea la base si no existe. Cuando el modelo se estabilice, pasar a migraciones (ver README).
    await db.Database.EnsureCreatedAsync();
    if (app.Environment.IsDevelopment())
    {
        // Si la base de desarrollo es de una versión anterior (sin las tablas nuevas), se recrea con los datos de ejemplo.
        try { await db.Usuarios.AnyAsync(); await db.TokensRestablecimiento.AnyAsync(); }
        catch (Exception)
        {
            app.Logger.LogWarning("La base de desarrollo era de una versión anterior: se recrea con datos de ejemplo.");
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
        await DatosDemo.CargarAsync(db);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();   // swagger
    app.UseSwaggerUI(); // swagger

    // Para probar los avisos sin esperar a la hora programada.
    app.MapPost("/api/avisos/procesar", (ProcesadorAvisos p) => p.ProcesarAsync()).WithTags("Desarrollo");
}

app.AvisarSiEnviosDeConsola();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapContrasenaEndpoints();
app.MapComercioEndpoints();
app.MapClienteEndpoints();

app.Run();

public partial class Program { }

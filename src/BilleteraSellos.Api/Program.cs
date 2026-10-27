using System.Text.Json.Serialization;
using BilleteraSellos.Api.Datos;
using BilleteraSellos.Api.Endpoints;
using BilleteraSellos.Api.Servicios;
using BilleteraSellos.Api.Tareas;
using Microsoft.EntityFrameworkCore;

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

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod())); // ajustar antes de producción
builder.Services.AddEndpointsApiExplorer(); // swagger
builder.Services.AddSwaggerGen();           // swagger

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Crea la base si no existe. Cuando el modelo se estabilice, pasar a migraciones (ver README).
    await db.Database.EnsureCreatedAsync();
    if (app.Environment.IsDevelopment()) await DatosDemo.CargarAsync(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();   // swagger
    app.UseSwaggerUI(); // swagger

    // Para probar los avisos sin esperar a la hora programada.
    app.MapPost("/api/avisos/procesar", (ProcesadorAvisos p) => p.ProcesarAsync()).WithTags("Desarrollo");
}

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapComercioEndpoints();
app.MapClienteEndpoints();

app.Run();

public partial class Program { }

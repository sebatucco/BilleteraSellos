# Billetera de Sellos

Tarjetas de sellos digitales de varios comercios en una sola billetera.
API en ASP.NET Core 8 + Entity Framework Core, con una pantalla web para probar la caja y la billetera.

## 1. Qué instalar en tu PC (una sola vez)

1. **.NET 8 SDK**: https://dotnet.microsoft.com/download/dotnet/8.0
2. Un editor, cualquiera de estos:
   - **Visual Studio 2022** (Community es gratis), con la carga de trabajo "ASP.NET y desarrollo web".
   - **VS Code** con la extensión **C# Dev Kit**.
3. Opcional: **Git**, para versionar el proyecto desde el día uno.

Para confirmar que quedó instalado, en una terminal:

```
dotnet --version
```

Tiene que mostrar un número que empiece con `8.`.

## 2. Correr el proyecto

```
cd BilleteraSellos/src/BilleteraSellos.Api
dotnet run
```

La primera vez descarga las librerías (EF Core, SQLite, PostgreSQL, Swagger) y crea la base `billetera.db` con datos de ejemplo.

Después abrí en el navegador:

- **http://localhost:5225** → la app: modo Cliente y modo Comercio.
- **http://localhost:5225/swagger** → todos los endpoints para probarlos uno por uno.

En Visual Studio: abrí `BilleteraSellos.sln` y apretá F5.

### Para probar

- **Cliente:** entrá con el DNI `30111222` (Lucía). Tiene tarjetas en 3 comercios.
- **Comercio:** elegí "Barbería Norte", buscá `30111222` y sumá un sello. Le falta 1 para completar. Después, en modo Cliente, canjeá el premio y copiá el código; en modo Comercio, entregalo.
- **Avisos automáticos:** en Swagger ejecutá `POST /api/avisos/procesar`. En la consola vas a ver el saludo de cumpleaños y los "te extrañamos".

Para empezar de cero con los datos de ejemplo, borrá `billetera.db` y volvé a correr.

## 3. Cómo está armado

```
src/BilleteraSellos.Api/
  Dominio/Entidades.cs        Comercio, ProgramaSellos, Cliente, Tarjeta, Movimiento, Canje, Aviso
  Datos/AppDbContext.cs       Acceso a la base (EF Core)
  Datos/DatosDemo.cs          Datos de ejemplo (solo en Development)
  Servicios/Reglas.cs         Niveles, clientes que se enfrían, calculadora de costo, códigos
  Servicios/Avisos.cs         Canal de avisos (hoy: consola) y hora local de Argentina
  Endpoints/ComercioEndpoints.cs   Lo que usa la caja
  Endpoints/ClienteEndpoints.cs    Lo que usa la billetera
  Tareas/AvisosAutomaticos.cs      Cumpleaños y reactivación, una vez por día
  wwwroot/index.html               Pantalla de prueba
```

### Reglas de negocio ya implementadas

| Regla | Dónde |
|---|---|
| Un cliente se registra una vez y usa todos los comercios | `POST /api/clientes` |
| Buscar en la caja por DNI, teléfono (con o sin +54 9) o QR | `GET /api/comercios/{id}/clientes/buscar` |
| La tarjeta se crea sola con el primer sello | `POST /api/comercios/{id}/sellos` |
| No se puede sellar dos veces seguidas (horas configurables) | `ProgramaSellos.HorasEntreSellos` |
| Tarjeta completa: no suma más hasta canjear | `POST /api/comercios/{id}/sellos` |
| Canje con código de 6 caracteres, sin letras confusas, con vencimiento | `POST /api/clientes/{id}/tarjetas/{tarjetaId}/canjear` |
| El código solo sirve en su comercio y una sola vez | `POST /api/comercios/{id}/canjes/{codigo}/entregar` |
| Niveles Bronce (0), Plata (10), Oro (30 sellos) por comercio | `Servicios/Reglas.cs` → `Niveles` |
| Clientes en riesgo según su propio ritmo (2x = riesgo, 3x = crítico) | `GET /api/comercios/{id}/clientes-en-riesgo` |
| Calculadora de costo del premio | `GET /api/comercios/{id}/costo-premio` |
| Cumpleaños próximos | `GET /api/comercios/{id}/cumpleanos` |
| Saludo de cumpleaños y "te extrañamos", sin repetir en 14 días | `Tareas/AvisosAutomaticos.cs` |
| El cliente acepta o no promos de cada comercio | `PUT /api/clientes/{id}/tarjetas/{tarjetaId}/promos` |
| Reseña de Google como link, sin premio a cambio | `Comercio.ResenaGoogleUrl` |

## 4. Pasar a PostgreSQL (cuando subas a un servidor)

En `appsettings.json`:

```json
"BaseDeDatos": { "Proveedor": "Postgres" },
"ConnectionStrings": { "Principal": "Host=...;Database=billetera;Username=...;Password=..." }
```

No hay que tocar código.

## 5. Lo que falta antes de tener clientes reales

En este orden:

1. **Seguridad (imprescindible).** Hoy cualquiera puede llamar a cualquier endpoint.
   - Comercios: usuarios con contraseña y roles (dueño / cajero), con ASP.NET Core Identity y JWT.
   - Clientes: login con código de un solo uso por SMS o WhatsApp.
   - Cada endpoint de comercio tiene que verificar que el usuario pertenece a ese comercio.
   - Restringir CORS al dominio real.
2. **Migraciones de EF Core** en lugar de `EnsureCreated`, para cambiar el modelo sin perder datos:
   ```
   dotnet tool install --global dotnet-ef
   dotnet ef migrations add Inicial
   ```
   Y en `Program.cs` cambiar `EnsureCreatedAsync()` por `MigrateAsync()`.
3. **Avisos reales.** Reemplazar `NotificadorConsola` por una implementación de `INotificador`:
   tarjetas de Google Wallet y Apple Wallet (avisos gratis en la pantalla bloqueada), y WhatsApp solo para lo que lo justifique.
4. **Billetera instalable.** Convertir la pantalla en PWA (manifest + service worker) o pasar a Blazor/.NET MAUI.
5. **Datos personales.** Términos, política de privacidad y consentimiento explícito al registrarse. En Argentina rige la Ley 25.326 de Protección de Datos Personales; conviene consultarlo con un profesional antes de lanzar.
6. **Hosting:** Azure App Service, Railway o Render, con PostgreSQL administrado.

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

Usuarios de ejemplo del panel (contraseña de todos: `Demo1234`):

| Email | Comercio | Rol |
|---|---|---|
| cafe@demo.com | Café Tostado | Dueña |
| caja@cafe.demo | Café Tostado | Cajero |
| barberia@demo.com | Barbería Norte | Dueño |
| panaderia@demo.com | Panadería La Espiga | Dueña |
| verduleria@demo.com | Verdulería Doña Rosa | Dueña |

- **Cliente:** entrá con el celular `1154873320` (Lucía, ya tiene tarjetas en 3 comercios). En desarrollo el código de verificación aparece en la misma pantalla y en la consola. Con cualquier otro número creás una cuenta nueva.
- **Comercio:** ingresá como `barberia@demo.com`, buscá el DNI `30111222` y sumá un sello: con eso Lucía completa la tarjeta. En modo Cliente canjeá el premio y copiá el código; en modo Comercio, entregalo.
- **Roles:** con `caja@cafe.demo` no aparecen las reglas de la tarjeta ni el equipo; con `cafe@demo.com` sí.
- **Avisos automáticos:** en Swagger ejecutá `POST /api/avisos/procesar`. En la consola vas a ver el saludo de cumpleaños y los "te extrañamos".
- **Swagger con login:** hacé login, copiá el `token` y pegalo en el botón **Authorize**.

Para empezar de cero con los datos de ejemplo, borrá `billetera.db` y volvé a correr. Si la base es de una versión anterior, en desarrollo se recrea sola.

## Cómo funciona el login

- **Comercios:** email y contraseña. Dos roles: **dueño** (todo, incluido cambiar las reglas y manejar el equipo) y **cajero** (sumar sellos, dar de alta clientes y entregar premios).
- **Clientes:** sin contraseña. Ponen su celular, reciben un código de 6 números por SMS y entran. Si es la primera vez, completan nombre, DNI y fecha de nacimiento.
- **Alta en la caja:** si alguien no tiene la app, el cajero lo registra con DNI y celular. Cuando después entre con ese celular, encuentra sus sellos.
- **Tokens:** cada login devuelve un token que se manda en `Authorization: Bearer <token>`. En la base solo se guarda su hash. Se cierra con `POST /api/auth/salir`, y desactivar un cajero corta su acceso al instante.
- **Protecciones:** contraseñas con PBKDF2-SHA256, bloqueo de 15 minutos tras 5 contraseñas incorrectas, códigos SMS que vencen a los 10 minutos con 5 intentos, máximo 5 códigos por hora por teléfono y 20 pedidos de login por minuto por IP. Cada comercio solo puede ver y tocar sus propios datos.

## 3. Cómo está armado

```
src/BilleteraSellos.Api/
  Dominio/Entidades.cs        Comercio, ProgramaSellos, Cliente, Tarjeta, Movimiento, Canje, Aviso
  Dominio/Seguridad.cs        Usuario (dueño/cajero), Sesion, CodigoVerificacion
  Seguridad/Autenticacion.cs  Contraseñas, tokens, sesiones y permisos por comercio
  Seguridad/CodigosSms.cs     Códigos de verificación por SMS
  Seguridad/Envios.cs         Envío por Twilio (SMS/WhatsApp) y SMTP, o consola en desarrollo
  Endpoints/AuthEndpoints.cs  Login, registro de comercios, equipo
  Endpoints/ContrasenaEndpoints.cs  Cambiar y recuperar contraseña
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
| Un cliente se registra una vez y usa todos los comercios | `POST /api/auth/clientes/codigo` → `verificar` → `POST /api/clientes` |
| El cajero da de alta a quien no tiene la app | `POST /api/comercios/{id}/clientes` |
| Buscar en la caja por DNI, teléfono (con o sin +54 9) o QR | `GET /api/comercios/{id}/clientes/buscar` |
| La tarjeta se crea sola con el primer sello | `POST /api/comercios/{id}/sellos` |
| No se puede sellar dos veces seguidas (horas configurables) | `ProgramaSellos.HorasEntreSellos` |
| Tarjeta completa: no suma más hasta canjear | `POST /api/comercios/{id}/sellos` |
| Canje con código de 6 caracteres, sin letras confusas, con vencimiento | `POST /api/billetera/tarjetas/{tarjetaId}/canjear` |
| El código solo sirve en su comercio y una sola vez | `POST /api/comercios/{id}/canjes/{codigo}/entregar` |
| Niveles Bronce (0), Plata (10), Oro (30 sellos) por comercio | `Servicios/Reglas.cs` → `Niveles` |
| Clientes en riesgo según su propio ritmo (2x = riesgo, 3x = crítico) | `GET /api/comercios/{id}/clientes-en-riesgo` |
| Calculadora de costo del premio | `GET /api/comercios/{id}/costo-premio` |
| Cumpleaños próximos | `GET /api/comercios/{id}/cumpleanos` |
| Saludo de cumpleaños y "te extrañamos", sin repetir en 14 días | `Tareas/AvisosAutomaticos.cs` |
| El cliente acepta o no promos de cada comercio | `PUT /api/billetera/tarjetas/{tarjetaId}/promos` |
| Reseña de Google como link, sin premio a cambio | `Comercio.ResenaGoogleUrl` |

## Configurar SMS y email

Por defecto los códigos SMS y los emails se escriben en la consola (sirve para desarrollar). Para enviarlos de verdad, completá `appsettings.json` o, mejor, variables de entorno en el servidor (así las claves no quedan en el repositorio; por ejemplo `Sms__Twilio__AuthToken`).

**SMS o WhatsApp con Twilio** (crear cuenta en https://www.twilio.com y comprar un número, o usar el sandbox de WhatsApp para probar):

```json
"Sms": {
  "Proveedor": "Twilio",
  "Twilio": {
    "AccountSid": "ACxxxxxxxxxxxxxxxx",
    "AuthToken": "tu-auth-token",
    "From": "+1XXXXXXXXXX",
    "Canal": "sms"
  }
}
```

- Con `"Canal": "whatsapp"` el código sale por WhatsApp (el `From` tiene que ser un número de WhatsApp habilitado en Twilio).
- Los teléfonos se aceptan como la gente los escribe (`011 15 5487-3320`, `+54 9 11 5487 3320`, `11 5487 3320`) y se envían como `+5491154873320`.
- Si Twilio falla, el cliente ve "No pudimos enviar el código..." y el detalle queda en el log, con el número oculto.
- Con SMS real configurado, la respuesta nunca incluye el código, ni siquiera en desarrollo.

**Email por SMTP** (sirve Gmail con contraseña de aplicación, Brevo, SendGrid, Amazon SES, etc.):

```json
"Email": {
  "Proveedor": "Smtp",
  "Smtp": { "Host": "smtp-relay.brevo.com", "Puerto": 587, "Usuario": "...", "Contrasena": "...", "De": "no-responder@tudominio.com", "UsarSsl": true }
},
"UrlPublica": "https://tudominio.com"
```

`UrlPublica` es la dirección del sitio, para armar el link de "crear contraseña nueva". Si falta un dato obligatorio, la aplicación no arranca y dice cuál. En producción, si SMS o email siguen en `Consola`, lo avisa en el log al arrancar.

## Contraseñas del panel

- **Cambiar:** en el panel, "Cambiar mi contraseña". Pide la actual y cierra las sesiones en otros dispositivos.
- **Olvidé mi contraseña:** desde el login. Llega un email con un link que vence en 1 hora y sirve una sola vez. Al usarlo, la cuenta se desbloquea y se cierran todas las sesiones. Se mandan como máximo 3 links por hora por usuario, y la respuesta es siempre la misma exista o no el email.

## 4. Pasar a PostgreSQL (cuando subas a un servidor)

En `appsettings.json`:

```json
"BaseDeDatos": { "Proveedor": "Postgres" },
"ConnectionStrings": { "Principal": "Host=...;Database=billetera;Username=...;Password=..." }
```

No hay que tocar código.

## 5. Lo que falta antes de tener clientes reales

En este orden:

1. **Cuentas de Twilio y SMTP.** El código ya está (ver "Configurar SMS y email"): falta crear las cuentas, cargar las claves en el servidor y probar con un celular real. Al publicar, configurar también `Cors:OrigenesPermitidos` y `UrlPublica` con el dominio real y servir todo por HTTPS.
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

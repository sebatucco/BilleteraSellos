# Billetera de Sellos

[![CI](https://github.com/sebatucco/BilleteraSellos/actions/workflows/ci.yml/badge.svg)](https://github.com/sebatucco/BilleteraSellos/actions/workflows/ci.yml)

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

En Visual Studio: abrí `BilleteraSellos.sln`, verificá que el proyecto de inicio sea **BilleteraSellos.Api** (clic derecho sobre él → **Establecer como proyecto de inicio**) y apretá F5.

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

## QR, cartel del mostrador y app instalable

- **QR del cliente:** en la billetera, el botón **Mostrar mi QR** lo abre a pantalla completa sobre fondo blanco y evita que la pantalla se apague mientras se muestra.
- **Escanear en la caja:** en el panel, **Escanear QR del cliente** abre la cámara. Usa el lector que trae el navegador (`BarcodeDetector`, en Chrome para Android) y, si no existe (Safari, Firefox), `jsQR`. Si la cámara no está disponible o se niega el permiso, se puede buscar por DNI o teléfono como siempre.
- **Cartel del mostrador:** en el panel, **Imprimir cartel** abre `cartel.html?c=<codigo>` con el nombre del comercio, el premio y un QR que lleva a `/#unirse=<codigo>`. El cliente lo escanea con la cámara, entra con su celular (o se registra) y la tarjeta se suma sola a su billetera.
- **App instalable (PWA):** desde el celular se puede "Agregar a la pantalla de inicio" y abre como una app. El service worker guarda solo la parte visual (páginas, scripts, íconos) para que abra rápido y sin conexión. **Nunca guarda respuestas de `/api`**, que tienen datos personales. Al publicar cambios en esos archivos, subí `VERSION` en `wwwroot/sw.js`.
- Las librerías de QR están en `wwwroot/lib` con sus licencias: qrcode-generator 1.4.4 (MIT) y jsQR 1.4.0 (Apache 2.0).

### Probar la cámara desde el celular

Los navegadores solo dejan usar la cámara (y el service worker) en **HTTPS** o en `localhost`. En la PC alcanza con `http://localhost:5225`. Para probar desde el celular en la misma red hay dos opciones:

1. **HTTPS local:** ejecutá `dotnet dev-certs https --trust` una vez y corré con `dotnet run --launch-profile https`. Después entrá desde el celular a `https://<IP-de-tu-PC>:7160`. Para que escuche en la red, agregá `--urls "https://0.0.0.0:7160"`. El celular va a avisar que el certificado no es de confianza: aceptalo solo para probar.
2. **Túnel (más simple):** con una herramienta como `cloudflared tunnel --url http://localhost:5225` o ngrok obtenés una dirección `https://...` pública que abre tu PC desde cualquier celular.

## 3. Cómo está armado

```
BilleteraSellos.sln
src/
  BilleteraSellos.Dominio/          El negocio, sin dependencias externas
    Comun/Resultado.cs                Resultado y Error (validación, no encontrado, conflicto...)
    Entidades/                        Comercio, ProgramaSellos, Cliente, Tarjeta, Canje, Usuario, Sesion...
                                      con sus reglas: tarjeta.SumarSello(), tarjeta.Canjear(), usuario.RegistrarFalla()...
    Reglas/                           Funciones puras: niveles, clientes que se enfrían, costo del premio,
                                      teléfonos, emails, contraseñas, cumpleaños, códigos
  BilleteraSellos.Aplicacion/       Los casos de uso (CQRS)
    Abstracciones/                    IAppDbContext, IReloj, INotificador, IEnviadorCodigos, IEnviadorEmail, IHasherContrasenas
    Mensajeria/                       ICommand, IQuery y sus handlers (sin MediatR)
    Seguridad/                        Tokens y sesiones
    Funcionalidades/                  Un archivo por caso de uso, agrupados por área:
      Acceso/                           login, registro de comercio, códigos SMS, contraseñas
      Equipo/                           cajeros del comercio
      Comercios/                        caja y panel: sumar sello, entregar premio, resumen, clientes en riesgo...
      Billetera/                        registro del cliente, billetera, sumar tarjeta, canjear
      Avisos/                           cumpleaños y "te extrañamos"
  BilleteraSellos.Infraestructura/  Lo que habla con el mundo de afuera
    Persistencia/                     AppDbContext (EF Core), configuraciones, migraciones, datos demo, DATABASE_URL
    Servicios/                        Reloj, hash de contraseñas, Twilio, SMTP, consola
  BilleteraSellos.Api/              La puerta de entrada HTTP
    Endpoints/                        Rutas finitas: arman el comando, llaman al handler y devuelven la respuesta
    Seguridad/                        Lectura del token, políticas por rol, "solo tu comercio"
    Tareas/AvisosAutomaticos.cs       Proceso diario en segundo plano
    wwwroot/                          Billetera y panel de prueba (PWA), cartel del mostrador
tests/
  BilleteraSellos.Dominio.Tests/    Tests unitarios de las reglas y entidades
  BilleteraSellos.Aplicacion.Tests/ Tests de los casos de uso contra SQLite en memoria
pruebas/humo.sh                     Prueba de punta a punta de la API (la corre el CI)
```

### Reglas de negocio ya implementadas

| Regla | Dónde |
|---|---|
| Un cliente se registra una vez y usa todos los comercios | `POST /api/auth/clientes/codigo` → `verificar` → `POST /api/clientes` |
| El cajero da de alta a quien no tiene la app | `POST /api/comercios/{id}/clientes` |
| Buscar en la caja por DNI, teléfono (con o sin +54 9) o QR | `GET /api/comercios/{id}/clientes/buscar` |
| La tarjeta se crea sola con el primer sello | `POST /api/comercios/{id}/sellos` |
| No se puede sellar dos veces seguidas (horas configurables) | `Tarjeta.SumarSello()` y `ProgramaSellos.HorasEntreSellos` |
| Tarjeta completa: no suma más hasta canjear | `POST /api/comercios/{id}/sellos` |
| Canje con código de 6 caracteres, sin letras confusas, con vencimiento | `POST /api/billetera/tarjetas/{tarjetaId}/canjear` |
| El código solo sirve en su comercio y una sola vez | `POST /api/comercios/{id}/canjes/{codigo}/entregar` |
| Niveles Bronce (0), Plata (10), Oro (30 sellos) por comercio | `Dominio/Reglas` → `Niveles` |
| Clientes en riesgo según su propio ritmo (2x = riesgo, 3x = crítico) | `GET /api/comercios/{id}/clientes-en-riesgo` |
| Calculadora de costo del premio | `GET /api/comercios/{id}/costo-premio` |
| Cumpleaños próximos | `GET /api/comercios/{id}/cumpleanos` |
| Saludo de cumpleaños y "te extrañamos", sin repetir en 14 días | `Aplicacion/Funcionalidades/Avisos` (lo dispara `Api/Tareas/AvisosAutomaticos.cs`) |
| El cliente acepta o no promos de cada comercio | `PUT /api/billetera/tarjetas/{tarjetaId}/promos` |
| Reseña de Google como link, sin premio a cambio | `Comercio.ResenaGoogleUrl` |

## Arquitectura

El proyecto sigue **Clean Architecture** con **CQRS**, en una versión liviana pensada para un equipo chico.

### Las cuatro capas y la regla de dependencias

```
Api  ──►  Infraestructura  ──►  Aplicacion  ──►  Dominio
```

Cada capa solo conoce a las de su derecha. El **Dominio** no conoce a nadie: no sabe que existe una base de datos, HTTP ni Twilio.

- **Dominio:** las entidades con su comportamiento y las reglas del negocio. Por ejemplo, `Tarjeta.SumarSello()` decide si la tarjeta está completa o si pasó poco tiempo desde el último sello. Las operaciones que pueden fallar devuelven un `Resultado` con un `Error` tipado en vez de tirar excepciones.
- **Aplicación:** los casos de uso. Cada uno es un comando (cambia algo) o una consulta (solo lee) con su handler. Acá se orquesta: buscar en la base, llamar al dominio, guardar, mandar el aviso. Lo externo se usa a través de interfaces (`IAppDbContext`, `IEnviadorCodigos`, etc.).
- **Infraestructura:** las implementaciones concretas de esas interfaces: EF Core, Twilio, SMTP, el reloj, el hash de contraseñas.
- **Api:** recibe el pedido HTTP, arma el comando con los datos de la ruta y del usuario logueado, llama al handler y traduce el `Resultado` a la respuesta (400, 404, 409, etc. con `{ "error": "..." }`). No tiene lógica de negocio.

### CQRS sin MediatR

Los comandos y consultas son records que implementan `ICommand<T>` o `IQuery<T>`, y cada uno tiene su handler (`ICommandHandler<,>` / `IQueryHandler<,>`). No se usa MediatR (desde 2025 tiene licencia comercial) ni ninguna otra librería: los handlers se registran solos al arrancar, escaneando el proyecto de Aplicación, y cada endpoint inyecta directamente el handler que necesita. Es explícito y fácil de seguir con "Ir a definición".

Para agregar un caso de uso nuevo: crear el archivo en `Funcionalidades/<Área>/`, con el comando o consulta, sus DTOs y el handler, y mapear el endpoint en `Api/Endpoints`. No hay que registrar nada a mano.

### EF Core para escribir y para leer

- **Comandos:** EF Core con seguimiento de cambios y transacciones. El mismo código funciona en SQLite (desarrollo) y PostgreSQL (producción).
- **Consultas:** también EF Core, con `AsNoTracking()` y proyecciones directas a los DTOs, que para este volumen de datos es igual de rápido.
- **Cuándo sumar Dapper:** si aparece una consulta pesada de verdad (por ejemplo, reportes de muchos meses para comercios grandes) se puede escribir solo esa consulta con SQL a mano y Dapper, dentro de su handler. Como las consultas ya están separadas de los comandos, el cambio no toca nada más.
- La capa de Aplicación usa `IAppDbContext` (las tablas de EF) en lugar de un repositorio por entidad: EF ya cumple ese rol y así se evita código repetido.

### Migraciones

La base de PostgreSQL se crea y se actualiza con **migraciones de EF Core**, que están en `src/BilleteraSellos.Infraestructura/Persistencia/Migraciones`. La API las aplica sola al arrancar. Con SQLite (tu PC) la base se crea directo desde el modelo y, en desarrollo, se recrea si quedó vieja.

Cuando cambies una entidad o una configuración, creá una migración nueva:

- **Visual Studio:** menú **Herramientas → Administrador de paquetes NuGet → Consola del Administrador de paquetes**, y ahí:
  ```
  Add-Migration NombreDelCambio -Project BilleteraSellos.Infraestructura -StartupProject BilleteraSellos.Api
  ```
- **Línea de comandos** (una sola vez `dotnet tool install --global dotnet-ef --version 8.0.10`):
  ```
  dotnet ef migrations add NombreDelCambio --project src/BilleteraSellos.Infraestructura --startup-project src/BilleteraSellos.Api --output-dir Persistencia/Migraciones
  ```
- **Sin instalar nada:** subí tus cambios a la rama `generar-migracion` (opcionalmente con el nombre en `pruebas/nombre-migracion.txt`). GitHub genera la migración y la commitea en esa rama (`.github/workflows/generar-migracion.yml`).

Las migraciones se generan para PostgreSQL. El CI falla si alguien cambia el modelo y se olvida de la migración.

### Tests

- `tests/BilleteraSellos.Dominio.Tests`: reglas y entidades, sin base de datos. Son instantáneos.
- `tests/BilleteraSellos.Aplicacion.Tests`: casos de uso de punta a punta contra SQLite en memoria, con dobles para el reloj, el SMS y el email.

En **Visual Studio**: menú **Prueba → Explorador de pruebas** y **Ejecutar todas las pruebas** (Ctrl+R, A). Por línea de comandos: `dotnet test`.

Además, `pruebas/humo.sh` prueba la API completa desde afuera (ver "Integración continua").

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

## 5. Correr todo con Docker (opcional)

Si tenés Docker Desktop, esto levanta la API y un PostgreSQL local, con los datos de ejemplo:

```
docker compose up --build
```

Abrí http://localhost:5225. Para borrar todo y empezar de cero: `docker compose down -v`.

## 6. Publicar en un servidor (Railway)

El proyecto ya trae un `Dockerfile`, así que Railway (o Render) lo construye solo desde GitHub.

1. Entrá a https://railway.app con tu cuenta de GitHub y creá un proyecto nuevo: **Deploy from GitHub repo** → `BilleteraSellos`.
2. En el mismo proyecto agregá una base: **New** → **Database** → **PostgreSQL**.
3. En el servicio de la API, pestaña **Variables**, cargá:

   | Variable | Valor |
   |---|---|
   | `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` (referencia a la base del paso 2) |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `Cors__OrigenesPermitidos__0` | el dominio desde donde se usa la app, si es distinto del de la API (ej. `https://billetera.tudominio.com`) |

   No hace falta configurar el puerto: la app lee `PORT`, que pone Railway. Tampoco la IP real ni el https: la imagen ya confía en los encabezados del proxy del hosting (`Proxy__ConfiarEnEncabezados=true`).
4. En **Settings** → **Networking**, tocá **Generate Domain** para tener una URL pública con https.
5. En **Settings** → **Healthcheck Path** poné `/salud`. Railway espera a que responda 200 antes de pasar el tráfico a la versión nueva.

En **Render** es igual: **New** → **Web Service** desde el repo (detecta el Dockerfile), una base **PostgreSQL**, la variable `DATABASE_URL` con la *Internal Database URL* de la base, `ASPNETCORE_ENVIRONMENT=Production` y *Health Check Path* `/salud`.

**Importante antes de lanzar:**

- En producción **no se cargan los datos de ejemplo** ni existe Swagger: la base arranca vacía y los comercios se registran con `POST /api/auth/comercios/registro`.
- La base de PostgreSQL se crea y se actualiza sola al arrancar, con las migraciones (ver "Migraciones" en Arquitectura). Si el servidor ya tenía una base creada por una versión anterior sin migraciones, borrala una vez antes de publicar.
- Mientras los códigos se escriban en la consola, en producción nadie recibe el SMS: hace falta el proveedor de SMS (punto 1).

### Integración continua

Cada push y cada pull request corren en GitHub Actions (`.github/workflows/ci.yml`):

- compila la solución en Release con los paquetes reales y corre los tests unitarios (los resultados quedan como artifact `resultados-tests`),
- revisa que no haya cambios del modelo sin migración (`dotnet ef migrations has-pending-model-changes`),
- levanta la API con SQLite y corre la prueba de humo `pruebas/humo.sh` (login, permisos entre comercios, roles, sellos, canjes, códigos, bloqueos, contraseñas),
- repite la prueba contra un PostgreSQL 16 real, conectando con `DATABASE_URL` y aplicando las migraciones sobre una base vacía,
- construye la imagen Docker y verifica que responde en `/salud`.

Para correr la prueba de humo en tu PC, con la API levantada en Development y la base recién creada:

```
bash pruebas/humo.sh http://localhost:5225
```

(Necesita `curl` y `jq`; en Windows, desde Git Bash o WSL. Antes, arrancá la API con la variable `Seguridad__PedidosLoginPorMinuto=1000`, porque la prueba hace muchos logins seguidos.)

## 7. Lo que falta antes de tener clientes reales

En este orden:

1. **Cuentas de Twilio y SMTP.** El código ya está (ver "Configurar SMS y email"): falta crear las cuentas, cargar las claves en el servidor y probar con un celular real. Al publicar, configurar también `Cors:OrigenesPermitidos` y `UrlPublica` con el dominio real y servir todo por HTTPS.
2. ~~Migraciones de EF Core~~ Hechas: ver "Migraciones" en Arquitectura.
3. **Avisos reales.** Reemplazar `NotificadorConsola` por una implementación de `INotificador`:
   tarjetas de Google Wallet y Apple Wallet (avisos gratis en la pantalla bloqueada), y WhatsApp solo para lo que lo justifique.
4. ~~Billetera instalable~~ Hecha como PWA. Más adelante, si hace falta, app nativa con .NET MAUI.
5. **Datos personales.** Términos, política de privacidad y consentimiento explícito al registrarse. En Argentina rige la Ley 25.326 de Protección de Datos Personales; conviene consultarlo con un profesional antes de lanzar.
6. **Hosting:** ya está preparado (Docker, `DATABASE_URL`, `PORT`, `/salud`, https detrás del proxy). Los pasos están en la sección 6.

# --- Compilación ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Primero solo los .csproj, para que la descarga de paquetes quede en caché entre builds.
COPY src/BilleteraSellos.Dominio/BilleteraSellos.Dominio.csproj src/BilleteraSellos.Dominio/
COPY src/BilleteraSellos.Aplicacion/BilleteraSellos.Aplicacion.csproj src/BilleteraSellos.Aplicacion/
COPY src/BilleteraSellos.Infraestructura/BilleteraSellos.Infraestructura.csproj src/BilleteraSellos.Infraestructura/
COPY src/BilleteraSellos.Api/BilleteraSellos.Api.csproj src/BilleteraSellos.Api/
RUN dotnet restore src/BilleteraSellos.Api/BilleteraSellos.Api.csproj
COPY src/ src/
RUN dotnet publish src/BilleteraSellos.Api/BilleteraSellos.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# --- Ejecución ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Carpeta escribible para SQLite, por si se corre sin PostgreSQL.
RUN mkdir -p /app/datos && chown -R $APP_UID /app/datos
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Proxy__ConfiarEnEncabezados=true \
    ConnectionStrings__Principal="Data Source=/app/datos/billetera.db"
EXPOSE 8080
# Usuario sin privilegios que ya trae la imagen oficial.
USER $APP_UID
ENTRYPOINT ["dotnet", "BilleteraSellos.Api.dll"]

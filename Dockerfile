# Imagen del sincronizador. Se construye desde la raíz del repositorio:  docker build -t sincro-tienda .

# --- Compilación -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilar
WORKDIR /origen

# Primero solo lo que decide las dependencias: la restauración de paquetes (lo más lento) se
# reutiliza de la caché de Docker mientras no cambien, aunque cambie el código.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Sincro.Dominio/Sincro.Dominio.csproj src/Sincro.Dominio/
COPY src/Sincro.Aplicacion/Sincro.Aplicacion.csproj src/Sincro.Aplicacion/
COPY src/Sincro.Infraestructura/Sincro.Infraestructura.csproj src/Sincro.Infraestructura/
COPY src/Sincro.Api/Sincro.Api.csproj src/Sincro.Api/
RUN dotnet restore src/Sincro.Api/Sincro.Api.csproj

COPY src ./src
RUN dotnet publish src/Sincro.Api/Sincro.Api.csproj -c Release --no-restore -o /publicado /p:UseAppHost=false

# --- Ejecución ---------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=compilar /publicado .

# Npgsql intenta negociar cifrado GSS con PostgreSQL y, sin esta librería, lo anota como error al arrancar
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Sin permisos de administrador: el usuario «app» viene en la imagen
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "Sincro.Api.dll"]

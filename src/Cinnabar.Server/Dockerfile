# Cinnabar server image.
# Build from the repository root:  docker build -f src/Cinnabar.Server/Dockerfile -t cinnabar .
# Run:                              docker run -p 6379:6379 -v cinnabar-data:/data cinnabar

# Runtime base. Visual Studio's fast-mode debugging also starts from this stage.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
# Snapshots live in a volume; create it before dropping root so the app user can write to it.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
USER $APP_UID
EXPOSE 6379
EXPOSE 9090

# Build stage. Project files are copied first so `dotnet restore` is cached until they change.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "./"]
COPY ["src/Cinnabar.Protocol/Cinnabar.Protocol.csproj", "src/Cinnabar.Protocol/"]
COPY ["src/Cinnabar.Storage/Cinnabar.Storage.csproj", "src/Cinnabar.Storage/"]
COPY ["src/Cinnabar.Server/Cinnabar.Server.csproj", "src/Cinnabar.Server/"]
RUN dotnet restore "src/Cinnabar.Server/Cinnabar.Server.csproj"
COPY . .
WORKDIR "/src/src/Cinnabar.Server"
RUN dotnet build "Cinnabar.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Cinnabar.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Cinnabar.Server.dll", "--dir", "/data"]

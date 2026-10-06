FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config .editorconfig LICENSE ./
COPY KeyLoad.slnx ./KeyLoad.slnx
COPY tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets ./tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets
COPY src/ ./src/
RUN dotnet restore src/KeyLoad.Server/KeyLoad.Server.csproj --disable-parallel
ARG KEYLOAD_RELEASE_VERSION
ARG KEYLOAD_ASSEMBLY_VERSION
ARG KEYLOAD_FILE_VERSION
RUN if [ -n "$KEYLOAD_RELEASE_VERSION" ]; then \
      dotnet publish src/KeyLoad.Server/KeyLoad.Server.csproj --no-restore --configuration Release --output /app/publish -p:UseAppHost=false -p:Version="$KEYLOAD_RELEASE_VERSION" -p:AssemblyVersion="$KEYLOAD_ASSEMBLY_VERSION" -p:FileVersion="$KEYLOAD_FILE_VERSION"; \
    else dotnet publish src/KeyLoad.Server/KeyLoad.Server.csproj --no-restore --configuration Release --output /app/publish -p:UseAppHost=false; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 KeyLoad__DataDirectory=/data
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data && chmod 700 /data
USER $APP_UID
EXPOSE 8080 11111
ENTRYPOINT ["dotnet", "KeyLoad.Server.dll"]

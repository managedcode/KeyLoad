FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /source
COPY . .
RUN dotnet restore benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj --disable-parallel
ARG KEYLOAD_RELEASE_VERSION
ARG KEYLOAD_ASSEMBLY_VERSION
ARG KEYLOAD_FILE_VERSION
RUN if [ -n "$KEYLOAD_RELEASE_VERSION" ]; then \
      dotnet publish benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj --no-restore --configuration Release --output /app/publish -p:UseAppHost=false -p:Version="$KEYLOAD_RELEASE_VERSION" -p:AssemblyVersion="$KEYLOAD_ASSEMBLY_VERSION" -p:FileVersion="$KEYLOAD_FILE_VERSION"; \
    else dotnet publish benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj --no-restore --configuration Release --output /app/publish -p:UseAppHost=false; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
USER $APP_UID
ENTRYPOINT ["dotnet", "KeyLoad.ComparisonHost.dll", "--output", "Detailed", "--report-trx", "--results-directory", "/reports/TestResults"]

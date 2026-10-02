FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config .editorconfig ./
COPY src/ ./src/
RUN dotnet restore src/KeyLoad.Server/KeyLoad.Server.csproj --disable-parallel
RUN dotnet publish src/KeyLoad.Server/KeyLoad.Server.csproj --no-restore --configuration Release --output /app/publish -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 KeyLoad__DataDirectory=/data
USER $APP_UID
EXPOSE 8080 11111
ENTRYPOINT ["dotnet", "KeyLoad.Server.dll"]

using KeyLoad;
using KeyLoad.Server;

SimdRuntime.RequireHardwareSupport();
var builder = WebApplication.CreateBuilder(args);
await ServerConfiguration.ConfigureBuilderAsync(builder);
await using var app = builder.Build();
ServerConfiguration.ConfigureApplication(app);
await app.RunAsync();

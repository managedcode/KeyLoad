using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class PostgresNativeComposition
{
    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PostgresSessionOptions.Section);
        if (!section.Exists())
        {
            return;
        }
        services.AddOptions<PostgresSessionOptions>().Bind(section)
            .Validate(value => { value.Validate(); return true; }).ValidateOnStart();
        services.AddSingleton<PostgresRetainedAdmission>();
        services.AddSingleton<PostgresCancellationRegistry>();
        services.AddSingleton(provider => new PostgresServerCertificate(
            provider.GetRequiredService<global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions>>()));
        services.AddSingleton<PostgresConnectionHandler>();
        services.AddSingleton<IConfigureOptions<KestrelServerOptions>, PostgresEndpointConfiguration>();
    }
}

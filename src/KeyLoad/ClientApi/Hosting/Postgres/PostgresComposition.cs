namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class PostgresComposition
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
        services.AddSingleton<PostgresConnectionHandler>();
        services.AddSingleton<PostgresEndpointConfiguration>();
        services.AddSingleton<IStartupFilter>(provider => provider.GetRequiredService<PostgresEndpointConfiguration>());
    }
}

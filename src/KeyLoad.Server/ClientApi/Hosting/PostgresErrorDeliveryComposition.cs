using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class PostgresErrorDeliveryComposition
{
    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PostgresErrorDeliveryProtocol.Configuration);
        if (!section.Exists())
        {
            return;
        }
        var entries = section.GetChildren().Take(PostgresErrorDeliveryProtocol.One + PostgresErrorDeliveryProtocol.One).ToArray();
        if (section.Value is not null || entries.Length != PostgresErrorDeliveryProtocol.One
            || entries[PostgresErrorDeliveryProtocol.PrimaryIndex].Key != PostgresErrorDeliveryProtocol.SessionSetting
            || entries[PostgresErrorDeliveryProtocol.PrimaryIndex].GetChildren().Any()
            || !Guid.TryParseExact(entries[PostgresErrorDeliveryProtocol.PrimaryIndex].Value,
                PostgresErrorDeliveryProtocol.IdentityFormat, out var session) || session == Guid.Empty)
        {
            throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid);
        }
        services.AddSingleton(provider => Create(provider, configuration, session));
    }

    private static PostgresErrorDeliveryProbe Create(IServiceProvider services, IConfiguration configuration, Guid session)
    {
        var runtime = services.GetRequiredService<ServerRuntimeOptions>();
        var original = RequestCqrsProbeOptionsReader.Read(configuration,
            runtime.ReplicaConfiguration.Value, runtime.Node.Value.AllowPrivateNetworkHttp);
        if (!original.Enabled || original.SessionId != session.ToString(PostgresErrorDeliveryProtocol.IdentityFormat))
        {
            throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid);
        }
        var files = new PostgresErrorDeliveryFiles(session, runtime.ReplicaConfiguration.Value.LocalId,
            runtime.RequestProbeExecution);
        _ = files.ReadArm();
        return new(files, runtime.RequestProbeExecution, services.GetRequiredService<TimeProvider>());
    }
}

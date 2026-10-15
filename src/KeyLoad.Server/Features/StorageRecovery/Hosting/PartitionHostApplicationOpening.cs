using OpenTelemetry;

namespace KeyLoad.Server;

internal static class PartitionHostApplicationOpening
{
    internal static Task OpenAsync(IServiceProvider services)
    {
        services.GetRequiredService<ITelemetryHostInitializer>().Initialize();
        return services.GetRequiredService<PartitionHostApplicationOwner>().OpenAsync();
    }
}

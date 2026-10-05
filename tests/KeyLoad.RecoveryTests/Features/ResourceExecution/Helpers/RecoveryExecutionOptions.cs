using KeyLoad.Core;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests;

/// <summary>Explicit validated native options composition for genuine test-owned engine and replica fixtures.</summary>
internal static class RecoveryExecutionOptions
{
    internal static IOptions<DatabaseLimits> DatabaseLimits(DatabaseLimits? configured = null)
    {
        var settings = configured ?? new DatabaseLimits();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<DueWorkExecutionOptions> DueWork()
    {
        var settings = new DueWorkExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<EventSourceExecutionOptions> EventSource()
    {
        var settings = new EventSourceExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaExecutionOptions> Replica(ReplicaExecutionOptions? configured = null)
    {
        var settings = configured ?? new ReplicaExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<PeerDiscoveryOptions> PeerDiscovery(PeerDiscoveryOptions? configured = null)
    {
        var settings = configured ?? new PeerDiscoveryOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaConfiguration> Configuration(ReplicaConfiguration configured)
    {
        configured.Validate();
        return Options.Create(configured);
    }
}

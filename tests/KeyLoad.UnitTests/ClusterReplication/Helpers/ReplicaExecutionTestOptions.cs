using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

/// <summary>Explicit validated native options composition for genuine replica regression fixtures.</summary>
internal static class ReplicaExecutionTestOptions
{
    internal static IOptions<ReplicaExecutionOptions> Execution(ReplicaExecutionOptions? configured = null)
    {
        var settings = configured ?? new ReplicaExecutionOptions();
        settings.Validate();
        return Options.Create(settings);
    }

    internal static IOptions<ReplicaConfiguration> Configuration(ReplicaConfiguration configured)
    {
        configured.Validate();
        return Options.Create(configured);
    }
}

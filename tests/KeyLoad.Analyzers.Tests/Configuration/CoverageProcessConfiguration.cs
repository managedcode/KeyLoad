using Microsoft.Extensions.Options;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

[KeyLoad.ConfigurationBinding]
internal static class CoverageProcessConfiguration
{
    internal static IOptions<CoverageProcessOptions> Capture()
    {
        var policy = new CoverageProcessOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(policy.ProcessTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(policy.CleanupTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaximumConcurrentProcesses);
        return Options.Create(policy);
    }
}

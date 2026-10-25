using Microsoft.Extensions.Options;

namespace KeyLoad.ClusterTests.Authorization;

internal static class FoundationOtlpClusterOptions
{
    internal static IOptions<FoundationOtlpClusterExecutionOptions> Create(FoundationOtlpClusterExecutionOptions? configured = null)
    {
        var value = configured ?? new FoundationOtlpClusterExecutionOptions();
        value.Validate();
        return Options.Create(value);
    }
}

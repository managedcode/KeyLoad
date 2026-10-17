using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Authenticated native bindings for one living multi-test database session.</summary>
[KeyLoad.ConfigurationBinding]
internal sealed record IsolatedHostSettings(BenchmarkSessionSelection Selection, ComparisonExecutionIdentity Identity,
    BenchmarkSessionWorker Worker, string OutputDirectory, string Storage, string RunId,
    string? UnsupportedReason, IsolatedHostNativeSettings? Native, IOptions<ComparisonHostExecutionOptions> HostExecution)
{
    internal static string Required(IConfiguration configuration, string key)
        => string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException(IsolatedHostConstants.Failure)
            : configuration[key]!;
}

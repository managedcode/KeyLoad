using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests;

internal static class UnitAppHostResourceOptions
{
    internal static IOptions<ScaleServerResourceOptions> Execution(ScaleServerResourceOptions? configured = null)
    {
        var value = configured ?? new ScaleServerResourceOptions();
        value.Validate();
        return Options.Create(value);
    }

    internal static IOptions<BenchmarkProvenanceOptions> Provenance() => Options.Create(new BenchmarkProvenanceOptions());
    internal static ScaleServerResourceSampleBudget Budget(int? maximumBytes = null)
        => new(Execution(), Provenance(), maximumBytes);
}

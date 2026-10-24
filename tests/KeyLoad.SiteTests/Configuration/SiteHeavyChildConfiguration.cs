using Microsoft.Extensions.Options;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

[KeyLoad.ConfigurationBinding]
internal static class SiteHeavyChildConfiguration
{
    internal static IOptions<SiteHeavyChildOptions> Bind(int capacity, int maximumQueued, TimeSpan admissionDeadline)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueued);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(admissionDeadline, TimeSpan.Zero);
        return Options.Create(new SiteHeavyChildOptions
        {
            Capacity = capacity,
            MaximumQueued = maximumQueued,
            AdmissionDeadline = admissionDeadline
        });
    }
}

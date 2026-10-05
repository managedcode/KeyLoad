using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceReadiness
{
    private const string Runner = "comparisons";
    private const string Bootstrap = "bootstrap";

    internal static async Task WaitAsync(DistributedApplication app, ContainerResource[] resources,
        CancellationToken token)
    {
        foreach (var resource in resources.Where(item => item.Name != Runner
                     && !item.Name.Contains(Bootstrap, StringComparison.Ordinal)))
        {
            await app.ResourceNotifications.WaitForResourceHealthyAsync(resource.Name,
                WaitBehavior.StopOnResourceUnavailable, token);
        }
    }
}

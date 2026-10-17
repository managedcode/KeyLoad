using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Checks the original imported images against the actual Aspire native resource graph.</summary>
internal static class IsolatedNativeReportAssertions
{
    private const string Runner = "comparisons";
    private const string Bootstrap = "bootstrap";
    private const string KeyLoad = "KeyLoad";
    private const string Mongo = "MongoDB";

    internal static async Task VerifyModelAsync(ContainerResource[] containers, BenchmarkSessionSelection selection,
        CancellationToken cancellationToken)
    {
        var receipt = await ComparisonImageReceipt.ReadAsync(cancellationToken);
        var runner = containers.Single(item => item.Name == Runner);
        await RequireImageAsync(runner, receipt.RunnerImage);
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        var native = containers.Where(item => item.Name != Runner && !item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        var bootstrap = containers.Where(item => item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        await Assert.That(native.Length).IsEqualTo(unsupported ? 0 : selection.NodeCount);
        await Assert.That(bootstrap.Length).IsEqualTo(selection.Target == Mongo ? 1 : 0);
        foreach (var node in native.Concat(bootstrap))
        {
            await Assert.That(node.TryGetContainerImageName(out var image)).IsTrue();
            await Assert.That(image).Contains(ComparisonImageProtocol.ImageDigestPrefix);
            if (selection.Target == KeyLoad)
            {
                await RequireImageAsync(node, receipt.ServerImage);
            }
        }
    }

    private static async Task RequireImageAsync(ContainerResource resource, string expected)
    {
        var digest = expected[(expected.LastIndexOf(ComparisonImageProtocol.ImageDigestPrefix, StringComparison.Ordinal)
            + ComparisonImageProtocol.ImageDigestPrefix.Length)..];
        await Assert.That(resource.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(digest);
    }

}

using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Checks the selected real database graph before execution client execution.</summary>
internal static class IsolatedReportAssertions
{
    private const int EmptyCount = 0;
    private const int SingleItemCount = 1;

    private const string RemovedWorkloadResource = "comparisons";
    private const string Bootstrap = "bootstrap";
    private const string Mongo = "MongoDB";
    private const string ImageDigest = "@sha256:";

    internal static async Task VerifyModelAsync(IResourceCollection resources, BenchmarkSessionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(resources.Any(resource => resource.Name == RemovedWorkloadResource)).IsFalse();
        var containers = resources.OfType<ContainerResource>().ToArray();
        await Assert.That(resources.OfType<ProjectResource>()).IsEmpty();
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        var execution = containers.Where(item => !item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        var bootstrap = containers.Where(item => item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        await Assert.That(execution.Length).IsEqualTo(unsupported ? EmptyCount : selection.NodeCount);
        await Assert.That(bootstrap.Length).IsEqualTo(selection.Target == Mongo ? SingleItemCount : EmptyCount);
        foreach (var node in execution.Concat(bootstrap))
        {
            await Assert.That(node.TryGetContainerImageName(out var image)).IsTrue();
            await Assert.That(image).Contains(ImageDigest);
        }
    }
}

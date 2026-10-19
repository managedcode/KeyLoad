using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Checks the selected real database graph before execution client execution.</summary>
internal static class IsolatedReportAssertions
{
    private const string RemovedWorkloadResource = "comparisons";
    private const string Bootstrap = "bootstrap";
    private const string KeyLoad = "KeyLoad";
    private const string Mongo = "MongoDB";
    private const string ServerProject = "KeyLoad.csproj";
    private const string ExternalImageDigest = "@sha256:";

    internal static async Task VerifyModelAsync(IResourceCollection resources, BenchmarkSessionSelection selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(resources.Any(resource => resource.Name == RemovedWorkloadResource)).IsFalse();
        var projects = resources.OfType<ProjectResource>().ToArray();
        var containers = resources.OfType<ContainerResource>().ToArray();
        if (selection.Target == KeyLoad)
        {
            await Assert.That(projects.Length).IsEqualTo(selection.NodeCount);
            await Assert.That(containers).IsEmpty();
            foreach (var project in projects)
            {
                await Assert.That(Path.GetFileName(project.GetProjectMetadata().ProjectPath)).IsEqualTo(ServerProject);
            }
            return;
        }
        await Assert.That(projects).IsEmpty();
        var unsupported = BenchmarkSessionContract.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        var execution = containers.Where(item => !item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        var bootstrap = containers.Where(item => item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        await Assert.That(execution.Length).IsEqualTo(unsupported ? 0 : selection.NodeCount);
        await Assert.That(bootstrap.Length).IsEqualTo(selection.Target == Mongo ? 1 : 0);
        foreach (var node in execution.Concat(bootstrap))
        {
            await Assert.That(node.TryGetContainerImageName(out var image)).IsTrue();
            await Assert.That(image).Contains(ExternalImageDigest);
        }
    }
}

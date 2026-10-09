namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-001: removed topology fails before acquisition and leaves healthy selection available.</summary>
internal sealed class NativeTopologyAdmissionTests
{
    private const string KeyLoadTarget = "KeyLoad";
    private const string NativeDirectory = "native";
    private const int RemovedNodeCount = 2;
    private const int ReplicatedNodeCount = 3;
    private const int ExpectedResourceCount = 4;

    [Test]
    public async Task TwoMembersRejectBeforeResourcesAndThreeMembersContinueHealthy()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        await Assert.That(async () =>
        {
            await model.BuildAsync(KeyLoadTarget, RemovedNodeCount);
        }).Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(model.Root, NativeDirectory))).IsFalse();
        await Assert.That(Directory.Exists(model.Output)).IsFalse();

        var resources = await model.BuildAsync(KeyLoadTarget, ReplicatedNodeCount);
        await Assert.That(resources.Length).IsEqualTo(ExpectedResourceCount);
        var runner = resources.Single(resource => resource.Name == IsolatedResourceTopologyFixture.RunnerName);
        var nodes = resources.Where(resource => resource != runner).ToArray();
        await Assert.That(nodes.Length).IsEqualTo(ReplicatedNodeCount);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(runner, nodes);
        await Assert.That(Directory.Exists(model.ProductionRoot)).IsFalse();
    }
}

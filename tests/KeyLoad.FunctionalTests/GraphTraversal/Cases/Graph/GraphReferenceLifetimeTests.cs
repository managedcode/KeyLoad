using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphReferenceLifetimeTests
{
    [Test]
    public async Task GenuineDropRecreateDeniesOldGraphReferencesThenFreshTraversalPathIncomingAndSearchRetainTwoColdReceipts()
    {
        var failures = new List<Exception>();
        var token = TestContext.Current!.Execution.CancellationToken;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var profile = await DropRuntimeProfile.CreateAsync(token);
            await ServerFailureObserver.ObserveAsync(() => GraphReferenceLifetimeFlow.RunAsync(profile, token), failures);
            if (failures.Count != 0)
            {
                profile.RetainRoots();
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

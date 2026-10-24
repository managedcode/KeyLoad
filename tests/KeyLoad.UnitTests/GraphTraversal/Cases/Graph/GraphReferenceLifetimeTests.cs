using KeyLoad.Server;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.GraphTraversal;

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

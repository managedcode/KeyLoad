using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SdkPolicySnapshotTests
{
    [Test]
    public async Task CallerMutationPreservesEachRetainedBudgetAndHealthyTransport()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(
            () => SdkPolicySnapshotFlow.RunAsync(failures, TestContext.Current!.Execution.CancellationToken), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

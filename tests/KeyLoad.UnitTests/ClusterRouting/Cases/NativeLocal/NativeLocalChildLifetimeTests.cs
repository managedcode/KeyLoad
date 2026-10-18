namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Verifies child identity, cancellation and joined settlement against production Orleans hosts.</summary>
internal sealed class NativeLocalChildLifetimeTests
{
    [Test]
    public Task VerifiedChildrenRefuseMismatchesShareOriginalLedgerAndJoinBeforeParentThenReplay()
        => NativeLocalChildLifetimeFlow.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}

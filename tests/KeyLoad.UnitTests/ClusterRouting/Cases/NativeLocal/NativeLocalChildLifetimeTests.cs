namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Selected local-child contract on native three-silo journals, not hosted D4 ACK qualification.</summary>
internal sealed class NativeLocalChildLifetimeTests
{
    [Test]
    public Task VerifiedChildrenRefuseMismatchesShareOriginalLedgerAndJoinBeforeParentThenReplay()
        => NativeLocalChildLifetimeFlow.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}

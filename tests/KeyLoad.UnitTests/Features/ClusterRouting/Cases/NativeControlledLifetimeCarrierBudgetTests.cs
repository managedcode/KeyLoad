namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeControlledLifetimeCarrierBudgetTests
{
    [Test]
    public Task EightKiBIssuerAndFullCarrierRetainNativeCompletionReplayAndTwoColdCuts()
        => ExecuteAsync(limits: null);

    [Test]
    public Task ActualEightKiBNativeOwnersRefuseOversizeBeforeEffectsThenCompleteReplayAndTwoColdCuts()
        => ExecuteAsync(new DatabaseLimits { MaxBatchBytes = NativeControlledLifetimePayloadBoundary.MaximumBytes });

    private static Task ExecuteAsync(DatabaseLimits? limits)
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            try
            {
                if (limits is not null)
                {
                    await Assert.That(source.Database.Limits.MaxBatchBytes).IsEqualTo(limits.MaxBatchBytes);
                    await Assert.That(target.Database.Limits.MaxBatchBytes).IsEqualTo(limits.MaxBatchBytes);
                }
                await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
                await NativeControlledLifetimeCarrierBudgetFlow.ExecuteAsync(source, target, listeners, corpus);
            }
            catch (Exception)
            {
                source.RetainRoot = true;
                target.RetainRoot = true;
                throw;
            }
        }, limits);
}

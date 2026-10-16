namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class NativeControlledLifetimeCarrierBudgetTests
{
    [Test]
    public Task ActualEightKiBNativeOwnersRefuseOversizeBeforeEffectsThenCompleteReplayAndTwoColdCuts()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            try
            {
                await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
                await NativeControlledLifetimeCarrierBudgetFlow.ExecuteAsync(source, target, listeners, corpus);
            }
            catch (Exception)
            {
                source.RetainRoot = true;
                target.RetainRoot = true;
                throw;
            }
        }, new DatabaseLimits { MaxBatchBytes = NativeControlledLifetimePayloadBoundary.MaximumBytes });
}

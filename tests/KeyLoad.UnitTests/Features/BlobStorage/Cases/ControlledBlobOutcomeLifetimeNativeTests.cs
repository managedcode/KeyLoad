using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

[NotInParallel]
internal sealed class ControlledBlobOutcomeLifetimeNativeTests
{
    [Test]
    public Task GenuineSixKindOutcomeCoverageRefusalsAndTwoColdOwnersPreserveOriginalReceiptsAtEightKiB()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            try
            {
                await ControlledBlobOutcomeLifetimeNativeFlow.ExecuteAsync(source, target, listeners, corpus,
                    TestContext.Current!.Execution.CancellationToken);
            }
            catch (Exception)
            {
                source.RetainRoot = true;
                target.RetainRoot = true;
                throw;
            }
        });
}

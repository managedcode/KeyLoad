using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

[NotInParallel]
internal sealed class SqlTypedInsertOwnershipEpochWholeFlowTests
{
    [Test]
    public Task ExplicitCallerFenceRetainsOriginalSqlAndInsertsAtPublishedOwnerAcrossTwoColdStores()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
    {
        SqlTypedInsertRetiredState? state = null;
            await ControlledPartitionMovementTerminalTrial.ExecuteWithAdditionalSeedAsync(source, target, listeners,
                corpus, async token =>
        {
            state = await SqlTypedInsertRetiredSeed.RunAsync(source, target, token);
        });
            await SqlTypedInsertOwnershipEpochFlow.RunAsync(source, target,
                state ?? throw new InvalidOperationException(MissingOriginal), TestContext.Current!.Execution.CancellationToken);
    });
    private const string MissingOriginal = "The genuine original SQL movement seed is absent.";
}


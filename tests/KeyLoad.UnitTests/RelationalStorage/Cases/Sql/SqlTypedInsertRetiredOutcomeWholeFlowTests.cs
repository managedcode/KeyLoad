using KeyLoad.UnitTests.Features.ClusterRouting;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

[NotInParallel]
internal sealed class SqlTypedInsertRetiredOutcomeWholeFlowTests
{
    [Test]
    public Task OriginalTypedInsertSurvivesRealRetirementCurrentPermissionFencesAndTwoColdOwners()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            SqlTypedInsertRetiredState? state = null;
            await ControlledPartitionMovementTerminalTrial.ExecuteWithAdditionalSeedAsync(source, target, listeners,
                corpus, async token => { state = await SqlTypedInsertRetiredSeed.RunAsync(source, target, token); });
            await SqlTypedInsertRetiredFlow.RunAsync(source, target,
                state ?? throw new InvalidOperationException(MissingOriginal), TestContext.Current!.Execution.CancellationToken);
        });
    private const string MissingOriginal = "The genuine original typed INSERT seed is absent.";
}

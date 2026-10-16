namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ResourceLifetimeGrantWholeFlowTests
{
    [Test]
    public async Task AuthenticStoredLifetimeGrantRefusesSubstitutionThenCompletesAndReplaysAcrossTwoColdOwners()
        => await ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            try
            {
                await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
                await ResourceLifetimeGrantFlow.ExecuteAsync(source, target, listeners, corpus);
            }
            catch (Exception)
            {
                source.RetainRoot = true;
                target.RetainRoot = true;
                throw;
            }
        });
}

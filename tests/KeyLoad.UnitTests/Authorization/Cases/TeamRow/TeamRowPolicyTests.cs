namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class TeamRowPolicyTests
{
    [Test]
    public async Task PersistedTeamMembershipRemovalRevocationRepairReplayAndSameRootColdAreComplete()
    {
        using var database = new TestDatabase();
        await TeamRowPolicyFlow.RunAsync(database, TestContext.Current!.Execution.CancellationToken);
    }
}

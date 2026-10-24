using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.OrleansExecution;

/// <summary>Captures the complete canonical source after its execution Orleans read barrier.</summary>
internal sealed record CommandObservedReturnEdgeSnapshot(string[] Rows, byte[] Identity,
    long Position, long Applied)
{
    internal static async Task<CommandObservedReturnEdgeSnapshot> CaptureAsync(
        CommandObservedPolicyScenario scenario, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        await fixture.Database.Coordinator.ReadBarrierAsync(token);
        return new(TeamRowPolicyAssertions.Image(fixture.Database.Store),
            OrleansSerialization.Serialize(fixture.Database.Store.Identity), fixture.Database.Store.Position,
            fixture.Database.Database.LastApplied);
    }

    internal async Task RequireSameAsync(CommandObservedPolicyScenario scenario, CancellationToken token)
    {
        var actual = await CaptureAsync(scenario, token);
        await Assert.That(actual.Rows.SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.Identity.SequenceEqual(Identity)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
    }
}

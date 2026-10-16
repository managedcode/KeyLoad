using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Captures the actual complete canonical source owner after its native RF3 barrier.</summary>
internal sealed record CommandObservedReturnEdgeSnapshot(string[] Rows, byte[] Identity,
    long Position, long Applied, (long LastIndex, long CommittedIndex, long Term) Journal)
{
    internal static async Task<CommandObservedReturnEdgeSnapshot> CaptureAsync(
        CommandObservedPolicyScenario scenario, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        await fixture.Database.Coordinator.ReadBarrierAsync(token);
        return new(TeamRowPolicyAssertions.Image(fixture.Database.Store),
            NativeSerialization.Serialize(fixture.Database.Store.Identity), fixture.Database.Store.Position,
            fixture.Database.Database.LastApplied, fixture.Database.ReadJournalCut());
    }

    internal async Task RequireSameAsync(CommandObservedPolicyScenario scenario, CancellationToken token)
    {
        var actual = await CaptureAsync(scenario, token);
        await Assert.That(actual.Rows.SequenceEqual(Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(actual.Identity.SequenceEqual(Identity)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(Position);
        await Assert.That(actual.Applied).IsEqualTo(Applied);
        await Assert.That(actual.Journal).IsEqualTo(Journal);
    }
}

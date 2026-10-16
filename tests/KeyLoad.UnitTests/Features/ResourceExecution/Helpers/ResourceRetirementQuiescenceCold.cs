using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceCold
{
    private static bool SameIdentity(StoreIdentity expected, StoreIdentity actual)
        => (expected with
        {
            SigningKey = ReadOnlyMemory<byte>.Empty
        }) == (actual with
        {
            SigningKey = ReadOnlyMemory<byte>.Empty
        })
            && expected.SigningKey.Span.SequenceEqual(actual.SigningKey.Span);

    internal static async Task ReopenAsync(ResourceRetirementQuiescenceState state, bool settled, CancellationToken token)
    {
        var before = state.Source.Database.ReadResourceRetirementPreparation(Root, state.Request,
            ResourceRetirementQuiescencePhases.Work(state.Source, token));
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var sourceIdentity = state.Source.Store.Identity;
        var targetIdentity = state.Target.Store.Identity;
        state.Source.Reopen();
        state.Target.Reopen();
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
        await Assert.That(SameIdentity(sourceIdentity, state.Source.Store.Identity)).IsTrue();
        await Assert.That(SameIdentity(targetIdentity, state.Target.Store.Identity)).IsTrue();
        await ResourceRetirementQuiescenceAssertions.EqualAsync(before,
            state.Source.Database.ReadResourceRetirementPreparation(Root, state.Request,
                ResourceRetirementQuiescencePhases.Work(state.Source, token)));
        await ResourceRetirementQuiescenceStatuses.ReadAsync(state, settled, token);
        await ResourceRetirementQuiescenceAssertions.ReplayAsync(state, token);
        await ResourceRetirementQuiescenceAssertions.ModelsAsync(state, token);
    }
}

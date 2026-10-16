using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsCompositionCold
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, AnalyticsCompositionCommit anchor,
        IReadOnlyList<AnalyticsCompositionCommit> commits)
    {
        var current = commits.ToList();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var identity = NativeSerialization.Serialize(owner.Store.Identity);
            var image = TeamRowPolicyAssertions.Image(owner.Store);
            var position = owner.Store.Position;
            var journal = owner.JournalCut();
            var applied = owner.Database.LastApplied;
            await owner.ReopenAsync();
            await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
            await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
            await Assert.That(owner.Store.Position).IsEqualTo(position);
            await Assert.That(owner.JournalCut()).IsEqualTo(journal);
            await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
            await AnalyticsCompositionOperations.ReplayAsync(owner, anchor);
            foreach (var commit in current)
            {
                await AnalyticsCompositionOperations.ReplayAsync(owner, commit);
            }

            await AnalyticsCompositionModels.RequireAsync(owner, current);
            await AnalyticsCompositionOutbox.RequireAsync(owner, anchor, current);
            var id = cycle == 0 ? AnalyticsCompositionData.ColdFirst : AnalyticsCompositionData.ColdSecond;
            var fresh = await AnalyticsCompositionFlow.HealthyAsync(owner, id, current.Count + 1);
            current.Add(fresh);
            await AnalyticsCompositionOperations.ReplayAsync(owner, fresh);
            await AnalyticsCompositionModels.RequireAsync(owner, current);
            await AnalyticsCompositionOutbox.RequireAsync(owner, anchor, current);
        }
    }
}

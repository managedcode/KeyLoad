using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class CurrentOwnershipTokenAfterMovementRf3ReadAssertions
{
    private const int NoChanges = 0;
    private const int OneChange = 1;
    private const int FirstChange = 0;
    private const long InitialPolicy = 0;

    internal static async Task WaitAsync(WaitForIndexResult actual, CommitToken minimum, long schema)
    {
        await Assert.That(actual.AppliedToken.Incarnation).IsEqualTo(minimum.Incarnation);
        await Assert.That(actual.AppliedToken.AtomicPartitionId).IsEqualTo(minimum.AtomicPartitionId);
        await Assert.That(actual.AppliedToken.OwnershipEpoch).IsEqualTo(minimum.OwnershipEpoch);
        await Assert.That(actual.AppliedToken.Position).IsGreaterThanOrEqualTo(minimum.Position);
        await Assert.That(actual.SchemaVersion).IsEqualTo(schema);
        await Assert.That(actual.PolicyEpoch).IsGreaterThan(InitialPolicy);
    }

    internal static async Task EmptyAsync(ChangeFeedPage page)
    {
        await Assert.That(page.Changes.Length).IsEqualTo(NoChanges);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.ThroughSequence).IsEqualTo(page.Tail);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    internal static async Task ChangeAsync(ChangeFeedPage page, CommitToken originalReceipt, DocumentResult expected)
    {
        await Assert.That(page.Changes.Length).IsEqualTo(OneChange);
        await Assert.That(page.HasMore).IsFalse();
        var change = page.Changes[FirstChange];
        await Assert.That(change.Reference).IsEqualTo(expected.Reference);
        await Assert.That(change.Revision).IsEqualTo(expected.Revision);
        await Assert.That(change.Deleted).IsFalse();
        await Assert.That(change.Before).IsNull();
        await SqlRf3Protocol.EqualAsync(expected, change.After);
        await SqlRf3Protocol.EqualAsync(originalReceipt, change.Commit);
        await Assert.That(page.ThroughSequence).IsEqualTo(change.Sequence);
        await Assert.That(page.Tail).IsEqualTo(change.Sequence);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }
}

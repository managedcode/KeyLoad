using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventingRequiredClaimState
{
    internal static Task ReadyAsync(RequiredProcessingPolicyFixture fixture, bool redacted, CancellationToken token)
        => RequiredProcessingPolicyAssertions.ObserveAsync(fixture.Harness.Store, async () =>
        {
            var metadata = new MessageMetadata("first", MessageState.Ready, 0, 1L, 1L, null, null);
            await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(metadata, fixture.Payload, fixture.Headers),
                fixture.Harness.Database.InspectMessage("root", fixture.Lane, "first"));
            await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(metadata,
                redacted ? "{\"value\":1}" : fixture.Payload, redacted ? "{\"trace\":\"one\"}" : fixture.Headers),
                fixture.Harness.Database.InspectMessage(RequiredProcessingPolicyFixture.Worker, fixture.Lane, "first"));
            await RequiredProcessingPolicyAssertions.EqualAsync<DocumentResult?>(null,
                fixture.Harness.Database.GetDocument("root", fixture.Reference("first"), cancellationToken: token));
            var foreign = fixture.Lane with
            {
                Partition = fixture.Harness.Partition with
                {
                    TenantId = "forged-tenant"
                }
            };
            MessageInspection? partial = null;
            var refusal = Assert.ThrowsExactly<KeyLoadException>(() => partial = fixture.Harness.Database.InspectMessage(
                RequiredProcessingPolicyFixture.Worker, foreign, "first"));
            await Assert.That(partial).IsNull();
            await Assert.That(refusal.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(refusal.Message).IsEqualTo("The principal cannot perform this operation in this scope.");
            await OutboxAsync(fixture.Harness.Database, fixture.Harness.Partition, 1L);
        });
    internal static Task FinalAsync(RequiredProcessingPolicyFixture fixture, DatabaseEngine database, ZoneTreeStore store, CancellationToken token)
        => RequiredProcessingPolicyAssertions.ObserveAsync(store, async () =>
        {
            await RequiredProcessingPolicyAssertions.EqualAsync(new MessageInspection(new("first", MessageState.Acked,
                1, 3L, 1L, null, null, null, 1L, null, 1L), null, null), database.InspectMessage("root", fixture.Lane, "first"));
            await RequiredProcessingPolicyAssertions.EqualAsync(new DocumentResult(fixture.Reference("first"), 1L,
                RequiredProcessingPolicyFixture.EffectJson("first"), false, []), database.GetDocument("root", fixture.Reference("first"), cancellationToken: token));
            await OutboxAsync(database, fixture.Harness.Partition, 2L);
        });
    private static async Task OutboxAsync(DatabaseEngine database, PartitionRef partition, long count)
    {
        var status = database.GetOutboxStatus("root", partition);
        await Assert.That(status.Head.Tail).IsEqualTo(count);
        await Assert.That(status.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(count);
        await Assert.That(status.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(status.Consumers).IsEmpty();
    }
}

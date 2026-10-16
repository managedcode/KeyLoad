using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsSharedFlow
{
    internal static void Configure(TestDatabase fixture)
    {
        var definition = new ResourceDefinition(P.Store, ResourceKind.AnalyticsStore, fixture.Partition.TransactionDomainId)
        {
            AnalyticsSchema = new([P.Dimension], [P.Metric])
        };
        var actual = fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition)).Get<ResourceDefinition>();
        if (!NativeSerialization.Serialize(definition).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual)))
        {
            throw new InvalidOperationException("The actual analytics resource definition differs.");
        }

        fixture.Configure(P.Documents, ResourceKind.Collection);
    }

    internal static async Task MixedAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        Configure(fixture);
        var anchor = new CommandRequest(Guid.NewGuid(), fixture.Partition, [new PutDocument(P.Documents, P.Anchor, P.Json, 0)]);
        var seed = await CommitAsync(fixture, anchor, [new(P.PutKind, P.Documents, P.Anchor, 1)]);
        var upsert = P.Upsert([new(fixture.Partition, P.Documents, P.Anchor)]);
        var put = new PutDocument(P.Documents, P.Effect, P.Json, 0);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [upsert, put]);
        var receipt = await CommitAsync(fixture, command,
            [new(AnalyticsStoreProtocol.UpsertKind, P.Store, P.EventId, 1), new(P.PutKind, P.Documents, P.Effect, 1)]);
        await AnalyticsSharedAssertions.BindingsAsync(fixture, command.CommandId);
        await AnalyticsSharedAssertions.PageAsync(fixture, upsert.Events.Single(), token);
        await AnalyticsSharedAssertions.ReplayAsync(fixture, command, receipt);
        var failed = new CommandRequest(Guid.NewGuid(), fixture.Partition,
            [P.Upsert(upsert.Events.Single().Links, 1), new PutDocument(P.Documents, P.Effect, P.Json, 0)]);
        var result = fixture.Submit(OperationKind.Batch, failed, id: failed.CommandId);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(result.SafeDetail).IsEqualTo("The expected revision does not match.");
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await AnalyticsSharedAssertions.PageAsync(fixture, upsert.Events.Single(), token);
        var seedTime = fixture.ReadRetainedEntry(anchor.CommandId).Operation!.EvaluatedAt;
        var commandTime = fixture.ReadRetainedEntry(command.CommandId).Operation!.EvaluatedAt;
        await SubscriptionOldCutOutbox.EntriesAsync(fixture.Database, fixture.Partition,
            [AnalyticsSharedAssertions.DocumentEntry(1, P.Anchor, seed.Token, fixture.Partition, seedTime),
             new(2, 0, receipt.Token, commandTime, upsert, new(AnalyticsStoreProtocol.UpsertKind, P.Store, P.EventId, 1)),
             AnalyticsSharedAssertions.DocumentEntry(3, P.Effect, receipt.Token, fixture.Partition, commandTime, 1)]);
    }

    internal static async Task<CommitReceipt> CommitAsync(TestDatabase fixture, CommandRequest command,
        System.Collections.Immutable.ImmutableArray<MutationReceipt> mutations)
    {
        var next = checked(fixture.ReadJournalCut().LastIndex + 1);
        var result = fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        var expected = new CommitReceipt(command.CommandId,
            new(fixture.Store.Identity.Incarnation, command.Partition.AtomicPartitionId, next, 1), mutations,
            DurabilityProfile.ProcessDurable);
        await AnalyticsSharedAssertions.SameAsync(expected, result);
        foreach (var mutation in result.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }

        return expected;
    }
}

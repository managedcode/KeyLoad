using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ResourceExecution.CachePolicyEpochProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CachePolicyEpochAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task ReadAsync(CachePolicyEpochState state, bool raw, bool updated, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var full = updated ? P.Updated : P.Original;
        var projected = raw ? full : updated ? P.UpdatedHidden : P.Hidden;
        var revision = updated ? P.UpdatedRevision : P.FirstRevision;
        await EqualAsync(new DocumentResult(state.Reference, revision, projected, !raw, raw ? [] : [P.Path]),
            state.Database.GetDocument(P.Reader, state.Reference, cancellationToken: token)!);
        await EqualAsync(new DocumentResult(state.Reference, revision, full, false, []),
            state.Database.GetDocument(P.Root, state.Reference, cancellationToken: token)!);
        await EqualAsync(state.Resource, state.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(state.Partition.TenantId, state.Partition.DatabaseId, P.Collection)))!);
        await EqualAsync(state.Reader, state.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(P.Reader)))!);
        await UnchangedAsync(state, image, position);
        await BoundedAsync(state);
    }

    internal static async Task UnchangedAsync(CachePolicyEpochState state, string[] image, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(state.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Store.Position).IsEqualTo(position);
    }

    internal static async Task BoundedAsync(CachePolicyEpochState state)
    {
        var pool = state.Owner.Budget.GetSnapshot();
        var cache = state.Store.GetPointCacheDiagnostics();
        await Assert.That(pool.RetainedBytes).IsLessThanOrEqualTo(P.PoolBytes);
        await Assert.That(pool.RetainedEntries).IsLessThanOrEqualTo(P.PoolEntries);
        await Assert.That(cache.RetainedBytes).IsGreaterThan(0L);
        await Assert.That(cache.RetainedBytes).IsLessThanOrEqualTo(P.OwnerBytes);
        await Assert.That(cache.ActivePins).IsEqualTo(0);
        await Assert.That(cache.InFlightEntries).IsEqualTo(0);
    }

    internal static async Task ReceiptAsync(CachePolicyEpochState state, CommandRequest command,
        CommitReceipt actual, long before, string kind, long revision)
    {
        CommitReceipt expected = new(command.CommandId, new(state.Store.Identity.Incarnation, state.Partition.AtomicPartitionId, before + 1L), [new(kind, P.Collection, P.Document, revision)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
    }
}

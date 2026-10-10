using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ResourceExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryRestoreAuthority
{
    internal const string Administrator = "ordered-administrator";
    private const string Wildcard = "*";
    private const string HistoricalMissing = "The original ordered restore authority is missing.";
    private const string HistoricalBound = "The ordered restore historical image exceeds its original bound.";

    internal static Task ProvisionAsync(DatabaseEngine database, QueueOrderedRetryState state)
        => ConfigureAsync(database, state, Expected(state, QueueOrderedRetryProtocol.One));

    internal static async Task RepairAsync(DatabaseEngine database, QueueOrderedRetryState state,
        QueueOrderedRetryState original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var previous = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(state.Principal)))
            ?? throw new InvalidOperationException(HistoricalMissing);
        await Assert.That(NativeSerialization.Serialize(previous)).IsEquivalentTo(
            NativeSerialization.Serialize(Expected(state, QueueOrderedRetryProtocol.One)), CollectionOrdering.Matching);
        CaptureHistorical(database, state, original);
        var image = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var denied = Expected(state, QueueOrderedRetryProtocol.Two) with
        { Grants = [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.QueueInspect)] };
        await ConfigureAsync(database, state, denied);
        var id = Guid.NewGuid();
        var secondVersion = state.ParkedHead == QueueParkedHeadPolicy.Continue ? QueueOrderedRetryProtocol.Two : QueueOrderedRetryProtocol.One;
        var request = new CommandRequest(id, state.Partition,
            [new CancelQueueMessage(state.Lane.Queue, QueueOrderedRetryProtocol.Second, secondVersion, QueueOrderedRetryProtocol.One),
             new RedriveQueueMessage(state.Lane.Queue, QueueOrderedRetryProtocol.First, QueueOrderedRetryProtocol.Six, QueueOrderedRetryProtocol.One)]);
        var operation = database.NormalizeOperation(new(id, OperationKind.Batch, state.Principal, state.Time,
            JsonSerializer.Serialize(request, JsonDefaults.Options)));
        await RefusedAsync(database, state, operation, image, ErrorCode.PermissionDenied);
        var key = KeySpace.PartitionOutcome(state.Partition, state.Principal, id);
        var rejectedBytes = database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(HistoricalMissing);
        if (rejectedBytes.Length + state.HistoricalOutcomes.Values.Sum(bytes => bytes.LongLength)
            + state.RestoredResourceBytes!.LongLength > UnitExecutionOptions.DatabaseLimits().Value.MaxBatchBytes)
        { throw new InvalidOperationException(HistoricalBound); }
        state.HistoricalOutcomes.Add(id, rejectedBytes);
        await ConfigureAsync(database, state, Expected(state, QueueOrderedRetryProtocol.Three));
        await RefusedAsync(database, state, operation, image, ErrorCode.PermissionDenied);
        foreach (var prior in original.Outcomes.Where(item => item.Operation.Kind == OperationKind.Batch && item.Result.Error is null))
        {
            await OriginalAuthorityRefusedAsync(database, state, prior.Operation, image);
            var current = database.NormalizeOperation(prior.Operation with { NativePayload = ReadOnlyMemory<byte>.Empty });
            await RefusedAsync(database, state, current, image, ErrorCode.TokenInvalidated);
        }
        await RequireAsync(database, state);
    }

    internal static async Task RequireAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(state.Principal)));
        await Assert.That(NativeSerialization.Serialize(principal)).IsEquivalentTo(
            NativeSerialization.Serialize(Expected(state, QueueOrderedRetryProtocol.Three)), CollectionOrdering.Matching);
        var resource = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Resource(state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue)));
        await Assert.That(resource).IsEquivalentTo(state.RestoredResourceBytes!, CollectionOrdering.Matching);
        foreach (var (id, original) in state.HistoricalOutcomes)
        {
            var bytes = database.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition, state.Principal, id)));
            await Assert.That(bytes).IsEquivalentTo(original, CollectionOrdering.Matching);
        }
    }

    private static PrincipalRecord Expected(QueueOrderedRetryState state, long epoch)
        => new(state.Principal, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.QueuePublish | Capability.QueueConsume
                | Capability.QueueAck | Capability.QueueRenew | Capability.QueueInspect | Capability.DeadLettersRead
                | Capability.DeadLettersRedrive | Capability.QueueCancel | Capability.Query)], [Wildcard])
        { ClusterAdministrator = true, PolicyEpoch = epoch };

    private static async Task ConfigureAsync(DatabaseEngine database, QueueOrderedRetryState state, PrincipalRecord principal)
    {
        var request = new ConfigurePrincipalRequest(principal);
        var operation = database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            QueueOrderedRetryProtocol.Root, state.Time, JsonSerializer.Serialize(request, JsonDefaults.Options)));
        var result = database.Apply(operation);
        await Assert.That(NativeSerialization.Serialize(result.Get<PrincipalRecord>())).IsEquivalentTo(
            NativeSerialization.Serialize(principal), CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Serialize(database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(principal.Id)))))
            .IsEquivalentTo(NativeSerialization.Serialize(principal), CollectionOrdering.Matching);
        state.Outcomes.Add((operation, result));
        await NativeReplayResultAssertions.Same<PrincipalRecord>(database.Apply(operation), result);
    }

    private static async Task RefusedAsync(DatabaseEngine database, QueueOrderedRetryState state,
        ReplicatedOperation operation, string[] image, ErrorCode expected)
    {
        var key = KeySpace.PartitionOutcome(state.Partition, state.Principal, operation.Id);
        var before = database.Store.Position;
        var prior = database.Store.Read(view => view.ReadOwnedValue(key));
        var result = database.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(expected);
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, image);
        if (prior is null)
        {
            await Assert.That(database.Store.Position).IsEqualTo(before + QueueOrderedRetryProtocol.One);
        }
        else
        {
            await Assert.That(database.Store.Position).IsEqualTo(before);
            var retained = database.Store.Read(view => view.ReadOwnedValue(key));
            await Assert.That(retained).IsEquivalentTo(prior, CollectionOrdering.Matching);
        }
    }

    private static async Task OriginalAuthorityRefusedAsync(DatabaseEngine database, QueueOrderedRetryState state,
        ReplicatedOperation operation, string[] image)
    {
        var before = database.Store.Position;
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(operation));
        var resolve = Assert.ThrowsExactly<KeyLoadException>(() => database.ResolveOutcome(operation));
        await Assert.That(apply.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(resolve.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, image);
    }

    private static void CaptureHistorical(DatabaseEngine database, QueueOrderedRetryState state, QueueOrderedRetryState original)
    {
        var remaining = UnitExecutionOptions.DatabaseLimits().Value.MaxBatchBytes;
        state.RestoredResourceBytes = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.Resource(state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue)))
            ?? throw new InvalidOperationException(HistoricalMissing);
        if (state.RestoredResourceBytes.Length > remaining)
        { throw new InvalidOperationException(HistoricalBound); }
        remaining -= state.RestoredResourceBytes.Length;
        foreach (var item in original.Outcomes.Where(item => item.Result.Error is null
            && item.Operation.Kind is OperationKind.Batch or OperationKind.Receive or OperationKind.Delivery))
        {
            var bytes = database.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(state.Partition, state.Principal, item.Operation.Id)))
                ?? throw new InvalidOperationException(HistoricalMissing);
            if (bytes.Length > remaining)
            { throw new InvalidOperationException(HistoricalBound); }
            remaining -= bytes.Length;
            state.HistoricalOutcomes.Add(item.Operation.Id, bytes);
        }
    }
}

using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.Messaging;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingBatch
{
    internal static CommandRequest Request(PartitionRef partition, string id)
        => new(Guid.NewGuid(), partition, [new PutDocument(Documents, id, DocumentJson, InitialRevision),
            new AppendEvents(Events, id, [new(id, EventType, EventJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, id, QueueJson)]);

    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        await HealthyAsync(state, BatchPrefix + Zero, token);
        await SchedulerDeniedAsync(state, token);
        foreach (var resource in Models)
        {
            var id = BatchPrefix + state.Mixed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var request = Request(state.Source.Partition, id);
            var issued = Issue(state.Source.Database, request);
            var actual = await ResourcePairBindingRecovery.ApplyAsync(state, issued, resource, token);
            await AcceptAsync(state, state.Source.Database, state.Source.Store, request, actual.Original, actual.Result,
                state.Source.ReadRetainedEntry(actual.Original).Index);
        }
        await StoredFailureAsync(state, token);
    }

    internal static ReplicatedOperation Issue(DatabaseEngine database, CommandRequest request)
        => database.CreateNativeOperation(OperationKind.Batch, request.CommandId, Root, database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));

    internal static async Task HealthyAsync(ResourcePairBindingState state, string id, CancellationToken token)
    {
        var request = Request(state.Source.Partition, id);
        var expectedIndex = state.Source.ReadJournalCut().LastIndex + One;
        var result = state.Source.SubmitIssuedEmbedded(Issue(state.Source.Database, request), token);
        var entry = state.Source.ReadRetainedEntry(request.CommandId);
        await Assert.That(entry.Index).IsEqualTo(expectedIndex);
        ResourcePairBindingAssertions.Remember(state, entry.Operation!, result);
        await AcceptAsync(state, state.Source.Database, state.Source.Store, request, entry.Operation!, result, expectedIndex);
    }

    internal static async Task AcceptAsync(ResourcePairBindingState state, DatabaseEngine database, KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        CommandRequest request, ReplicatedOperation original, OperationResult result, long index)
    {
        var id = ((PutDocument)request.Mutations[Zero]).Id;
        var expected = ResourcePairBindingAssertions.Receipt(store, request.Partition, request.CommandId, index,
            [new(PutKind, Documents, id, FirstRevision), new(AppendKind, Events, id, FirstRevision), new(EnqueueKind, Queue, id, FirstRevision)]);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, result.Get<CommitReceipt>());
        state.Mixed.Add((id, state.Mixed.Count + One, original.EvaluatedAt));
        await ResourcePairBindingAssertions.ModelsAsync(state, database);
    }

    private static async Task SchedulerDeniedAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        var principal = new PrincipalRecord(SchedulerAdmin, source.Partition.TenantId, [], []) { ClusterAdministrator = true };
        _ = source.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var damaged = ResourcePairBindingFaults.Install(state, Queue);
        var interval = TimeSpan.FromMinutes(One);
        var schedule = new ConfigureRecurringSchedule(new(new(source.Partition, Queue), Guid.NewGuid(),
            source.Database.EvaluationClock.GetUtcNow() + interval, interval, Utc, RecurringMisfirePolicy.CatchUp,
            QueueJson, Headers), InitialRevision);
        var request = new CommandRequest(Guid.NewGuid(), source.Partition, [schedule]);
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, SchedulerAdmin,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var before = QueueWholeFlowStorage.Bytes(source.Store);
        var actual = source.SubmitIssuedEmbedded(operation, token);
        var original = source.ReadRetainedEntry(request.CommandId).Operation!;
        await ResourceIncarnationAssertions.ResultAsync(new(null, ErrorCode.PermissionDenied, DeniedDetail), actual);
        ResourcePairBindingAssertions.Remember(state, original, actual);
        await ResourcePairBindingAssertions.FailureDeltaAsync(source.Store, before, source.Partition, original);
        ResourcePairBindingFaults.Repair(source.Store, damaged);
        var image = QueueWholeFlowStorage.Bytes(source.Store);
        var position = source.Store.Position;
        var journal = source.ReadJournalCut();
        await ResourceIncarnationAssertions.ResultAsync(actual, source.SubmitIssued(original));
        await ResourcePairBindingAssertions.UnchangedAsync(source.Store, image, position);
        await Assert.That(source.ReadJournalCut()).IsEqualTo(journal);
        await ResourcePairBindingAssertions.ModelsAsync(state, source.Database);
    }

    private static async Task StoredFailureAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), state.Source.Partition,
            [new PutDocument(Documents, state.Mixed[Zero].Id, Headers, InitialRevision)]);
        var before = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var result = state.Source.SubmitIssuedEmbedded(Issue(state.Source.Database, request), token);
        var original = state.Source.ReadRetainedEntry(request.CommandId).Operation!;
        await ResourceIncarnationAssertions.ResultAsync(new(null, ErrorCode.RevisionConflict, RevisionDetail), result);
        ResourcePairBindingAssertions.Remember(state, original, result);
        await ResourcePairBindingAssertions.FailureDeltaAsync(state.Source.Store, before, state.Source.Partition, original);
        var damagedRow = ResourcePairBindingFaults.Install(state, Documents);
        var damaged = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var damagedPosition = state.Source.Store.Position;
        await Assert.That(state.Source.Database.ResolveOutcome(original).Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(ResourcePairBindingAssertions.Outcome(state.Source.Store, state.Source.Partition, original)
            .AsSpan().SequenceEqual(state.Originals.Last().Outcome)).IsTrue();
        await ResourcePairBindingAssertions.UnchangedAsync(state.Source.Store, damaged, damagedPosition);
        ResourcePairBindingFaults.Repair(state.Source.Store, damagedRow);
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var position = state.Source.Store.Position;
        await ResourceIncarnationAssertions.ResultAsync(result, state.Source.SubmitIssued(original));
        await ResourcePairBindingAssertions.UnchangedAsync(state.Source.Store, image, position);
        await ResourcePairBindingAssertions.ModelsAsync(state, state.Source.Database);
    }
}

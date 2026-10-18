using KeyLoad.Core;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.DocumentStorage;
using P = KeyLoad.UnitTests.Features.ClientApi.SdkNativeProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkNativeProblemBoundaryAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task RefusalAsync(GrainOperationReply reply)
    {
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(P.Denied);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
    }
    internal static async Task ReceiptAsync(SdkNativeProblemBoundaryScenario state, CommandRequest command,
        CommitReceipt receipt, bool root)
    {
        var db = state.Native.Fixture.Database;
        var entry = db.ReadStoredOutcome(command.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(operation.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(operation.UserId!).IsEqualTo(root ? P.Root : state.Principal.Id);
        await EqualAsync(command, NativeSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span));
        var placement = db.Database.ReadAtomicPartitionPlacement(P.Root, db.Partition);
        MutationReceipt[] mutations = root
            ? [new(P.Put, state.Forbidden.Collection, P.Document, P.FirstRevision), new(P.Put, P.Allowed, P.Document, P.FirstRevision)]
            : [new(P.Put, P.Allowed, P.Document, P.HealthyRevision)];
        var expected = new CommitReceipt(command.CommandId, new(placement.Incarnation, command.Partition.AtomicPartitionId, entry.CanonicalSequence), [.. mutations], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(expected, receipt);
        await Assert.That(receipt.Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
    }
    internal static async Task CurrentAsync(SdkNativeProblemBoundaryScenario state)
    {
        var db = state.Native.Fixture.Database;
        await EqualAsync(new DocumentResult(state.Forbidden, P.FirstRevision, P.SeedJson, false, []),
            db.Database.GetDocument(P.Root, state.Forbidden));
        await EqualAsync(new DocumentResult(state.Allowed, state.HealthyReceipt is null ? P.FirstRevision : P.HealthyRevision,
            state.HealthyReceipt is null ? P.SeedJson : P.HealthyJson, false, []), db.Database.GetDocument(P.Root, state.Allowed));
        await SdkNativeProblemBoundaryOutbox.RequireAsync(state);
    }
    internal static async Task FailedOutcomeAsync(SdkNativeProblemBoundaryScenario state)
    {
        var db = state.Native.Fixture.Database;
        var original = db.ReadStoredOutcome(state.Denied.CommandId).PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(original.CommandId).IsEqualTo(state.Denied.CommandId);
        await Assert.That(original.UserId!).IsEqualTo(state.Principal.Id);
        await EqualAsync(state.Denied, NativeSerialization.Deserialize<CommandRequest>((original).Value!.Input.Span));
        var result = OutcomeStoreOracle.ReadPartition(db.Store, db.Partition, state.Principal.Id, state.Denied.CommandId);
        await Assert.That(result is not null).IsTrue();
        await Assert.That(result!.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.SafeDetail).IsEqualTo(P.Denied);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.Value).IsNull();
    }
    internal static SdkNativeProblemBoundaryCut Capture(SdkNativeProblemBoundaryScenario state)
    {
        var db = state.Native.Fixture.Database;
        var page = db.Store.Read(view => view.Scan([], P.MaximumRows));
        if (page.HasMore)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var rows = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + P.RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
        var failed = db.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(db.Partition,
            state.Principal.Id, state.Denied.CommandId))) ?? throw new InvalidOperationException(P.Failure);
        return new(rows, db.Store.Position, db.ReadCanonicalCut(), NativeSerialization.Serialize(db.Store.Identity),
            DocumentCommandOutcomeTestSupport.CaptureOutbox(db.Store, db.Database, db.Partition), failed);
    }
    internal static async Task SameAsync(SdkNativeProblemBoundaryScenario state, SdkNativeProblemBoundaryCut expected)
    {
        await CurrentAsync(state);
        var actual = Capture(state);
        await Assert.That(actual.Rows.SequenceEqual(expected.Rows)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Journal).IsEqualTo(expected.Journal);
        await Assert.That(actual.Identity.SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(actual.FailedOutcome.SequenceEqual(expected.FailedOutcome)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(actual.Outbox, expected.Outbox)).IsTrue();
    }
}

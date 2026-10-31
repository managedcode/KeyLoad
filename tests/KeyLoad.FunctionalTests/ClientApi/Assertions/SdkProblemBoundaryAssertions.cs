using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.DocumentStorage;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using P = KeyLoad.FunctionalTests.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class SdkProblemBoundaryAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task RefusalAsync(GrainOperationReply reply)
    {
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(reply.SafeDetail).IsEqualTo(P.Denied);
        await Assert.That(reply.Result).IsNull();
    }
    internal static async Task ReceiptAsync(SdkProblemBoundaryScenario state, CommandRequest command,
        CommitReceipt receipt, bool root)
    {
        var db = state.Execution.Fixture.Database;
        var entry = db.ReadStoredOutcome(command.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(operation.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(operation.UserId!).IsEqualTo(root ? P.Root : state.Principal.Id);
        await EqualAsync(command, ((CommandRequest)(operation).Value!.Input));
        var expectedToken = (root ? state.SeedToken : state.HealthyToken) ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(expectedToken.Position);
        MutationReceipt[] mutations = root
            ? [new(P.Put, state.Forbidden.Collection, P.Document, P.FirstRevision), new(P.Put, P.Allowed, P.Document, P.FirstRevision)]
            : [new(P.Put, P.Allowed, P.Document, P.HealthyRevision)];
        var copies = state.Execution.Fixture.Source.Services.GetRequiredService<IOptions<NodeOptions>>().Value.ReplicaCount;
        var expected = new CommitReceipt(command.CommandId, expectedToken, [.. mutations], DurabilityProfile.ProcessDurable, copies);
        await EqualAsync(expected, receipt);
        await Assert.That(receipt.Mutations.All(item => item.CompositionReferences.IsEmpty)).IsTrue();
    }
    internal static async Task CurrentAsync(SdkProblemBoundaryScenario state)
    {
        var db = state.Execution.Fixture.Database;
        await EqualAsync(new DocumentResult(state.Forbidden, P.FirstRevision, P.SeedJson, false, []),
            db.Database.GetDocument(P.Root, state.Forbidden));
        await EqualAsync(new DocumentResult(state.Allowed, state.HealthyReceipt is null ? P.FirstRevision : P.HealthyRevision,
            state.HealthyReceipt is null ? P.SeedJson : P.HealthyJson, false, []), db.Database.GetDocument(P.Root, state.Allowed));
        await SdkProblemBoundaryOutbox.RequireAsync(state);
    }
    // The denied command is applied as a failed outcome: its effects are reset and the refusal itself is retained.
    internal static async Task RetainedRefusalAsync(SdkProblemBoundaryScenario state)
    {
        var db = state.Execution.Fixture.Database;
        var outcome = OutcomeStoreOracle.ReadPartition(db.Store, db.Partition, state.Principal.Id, state.Denied.CommandId)
            ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(outcome.IsFailed).IsTrue();
        await Assert.That(outcome.Value).IsNull();
        await EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, P.Denied), outcome.Problem);
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.UnknownKey(state.Principal.Id, state.Denied.CommandId)))).IsNull();
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.GlobalKey(state.Principal.Id, state.Denied.CommandId)))).IsNull();
    }
    internal static SdkProblemBoundaryCut Capture(SdkProblemBoundaryScenario state)
    {
        var db = state.Execution.Fixture.Database;
        var page = db.Store.Read(view => view.Scan([], P.MaximumRows));
        if (page.HasMore)
        {
            throw new InvalidOperationException(P.Failure);
        }
        var rows = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + P.RowSeparator + Convert.ToHexString(row.Value.Span)).ToArray();
        return new(rows, db.Store.Position, db.ReadCanonicalCut(), OrleansSerialization.Serialize(db.Store.Identity),
            DocumentCommandOutcomeTestSupport.CaptureOutbox(db.Store, db.Database, db.Partition));
    }
    internal static async Task SameAsync(SdkProblemBoundaryScenario state, SdkProblemBoundaryCut expected)
    {
        await CurrentAsync(state);
        await RetainedRefusalAsync(state);
        var actual = Capture(state);
        await Assert.That(actual.Rows.SequenceEqual(expected.Rows)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Cut).IsEqualTo(expected.Cut);
        await Assert.That(actual.Identity.SequenceEqual(expected.Identity)).IsTrue();
        await Assert.That(DocumentCommandOutcomeTestSupport.OutboxEqual(actual.Outbox, expected.Outbox)).IsTrue();
    }
}

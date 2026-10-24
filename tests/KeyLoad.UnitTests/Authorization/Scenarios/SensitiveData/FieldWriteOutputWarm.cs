using KeyLoad.UnitTests.Messaging;
using P = KeyLoad.UnitTests.Authorization.FieldWriteOutputProtocol;

namespace KeyLoad.UnitTests.Authorization;

internal static class FieldWriteOutputWarm
{
    internal static async Task RunAsync(FieldWriteOutputState state, bool ancestor, CancellationToken token)
    {
        var db = state.Owner;
        state.Resource = db.Configure(P.Collection, ResourceKind.Collection,
            fields: [new(P.Path, P.Classification, P.ReadGrant, P.UseGrant, P.WriteGrant)]);
        PrincipalRecord writer = new(P.Writer, db.Partition.TenantId,
            [new(db.Partition.DatabaseId, P.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], []);
        var reader = writer with
        {
            Id = P.Reader,
            FieldGrants = [P.ReadGrant]
        };
        await ConfigureAsync(db, writer);
        await ConfigureAsync(db, reader);
        state.Original = new(Guid.NewGuid(), db.Partition, [new PutDocument(P.Collection, P.Document, P.Original)]);
        var before = state.Store.Position;
        var expectedToken = db.NextCommitToken();
        state.OriginalReceipt = state.Apply(state.Original, P.Root, token).Get<CommitReceipt>();
        await FieldWriteOutputAssertions.ReceiptAsync(state, state.Original, state.OriginalReceipt,
            before, expectedToken, P.Put, P.OriginalRevision);
        state.Outcomes.Add(state.Original.CommandId, state.Outcome(state.Original, P.Root));
        await FieldWriteOutputAssertions.CurrentAsync(state, false, token);
        state.Denied = Patch(state, ancestor, P.OriginalRevision);
        await FieldWriteOutputAssertions.FailureAsync(state.Apply(state.Denied, P.Writer, token), P.Denied);
        state.Outcomes.Add(state.Denied.CommandId, state.Outcome(state.Denied, P.Writer));
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        before = state.Store.Position;
        await FieldWriteOutputAssertions.FailureAsync(state.Apply(state.Denied, P.Writer, token), P.Denied);
        await FieldWriteOutputAssertions.UnchangedAsync(state, image, before);
        await FieldWriteOutputAssertions.CurrentAsync(state, false, token);
        await ConfigureAsync(db, writer with
        {
            PolicyEpoch = P.RepairedEpoch,
            FieldGrants = [P.WriteGrant]
        });
        state.Healthy = Patch(state, ancestor, P.OriginalRevision);
        before = state.Store.Position;
        expectedToken = db.NextCommitToken();
        state.HealthyReceipt = state.Apply(state.Healthy, P.Writer, token).Get<CommitReceipt>();
        await FieldWriteOutputAssertions.ReceiptAsync(state, state.Healthy, state.HealthyReceipt,
            before, expectedToken, P.Patch, P.CurrentRevision);
        state.Outcomes.Add(state.Healthy.CommandId, state.Outcome(state.Healthy, P.Writer));
        state.ReaderDenied = Patch(state, ancestor, P.CurrentRevision);
        await FieldWriteOutputAssertions.FailureAsync(state.Apply(state.ReaderDenied, P.Reader, token), P.Denied);
        state.Outcomes.Add(state.ReaderDenied.CommandId, state.Outcome(state.ReaderDenied, P.Reader));
        await FieldWriteOutputAssertions.CurrentAsync(state, true, token);
        await FieldWriteOutputAssertions.ReplayAsync(state, token);
    }

    private static CommandRequest Patch(FieldWriteOutputState state, bool ancestor, long revision)
        => new(Guid.NewGuid(), state.Owner.Partition, [new PatchDocument(P.Collection, P.Document,
            [new(ancestor ? P.ParentPath : P.Path, PatchKind.Set, ancestor ? P.ParentValue : P.Value)], revision)]);

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord expected)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await FieldWriteOutputAssertions.EqualAsync(expected, actual);
    }
}

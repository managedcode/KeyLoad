using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.Authorization.FieldWriteOutputProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class FieldWriteOutputAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task ReceiptAsync(FieldWriteOutputState state, CommandRequest command,
        CommitReceipt actual, long before, string kind, long revision)
    {
        CommitReceipt expected = new(command.CommandId, new(state.Store.Identity.Incarnation, state.Owner.Partition.AtomicPartitionId, before + P.NextPosition), [new(kind, P.Collection, P.Document, revision)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
    }

    internal static async Task CurrentAsync(FieldWriteOutputState state, bool updated, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var revision = updated ? P.CurrentRevision : P.OriginalRevision;
        var body = updated ? P.Updated : P.Original;
        DocumentResult hidden = new(state.Reference, revision, P.Hidden, true, [P.Path]);
        DocumentResult full = new(state.Reference, revision, body, false, []);
        await EqualAsync(hidden, state.Database.GetDocument(P.Writer, state.Reference, cancellationToken: token)!);
        await EqualAsync(full, state.Database.GetDocument(P.Reader, state.Reference, cancellationToken: token)!);
        await EqualAsync(full, state.Database.GetDocument(P.Root, state.Reference, cancellationToken: token)!);
        var resource = state.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(state.Owner.Partition.TenantId, state.Owner.Partition.DatabaseId, P.Collection)));
        await EqualAsync(state.Resource, resource!);
        await UnchangedAsync(state, image, position);
    }

    internal static async Task FailureAsync(Result<object> actual, string detail)
    {
        await EqualAsync(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, detail)), actual);
        await Assert.That(actual.Value).IsNull();
    }

    internal static async Task UnchangedAsync(FieldWriteOutputState state, string[] image, long position)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(state.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Store.Position).IsEqualTo(position);
    }

    internal static async Task ReplayAsync(FieldWriteOutputState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var original = state.Apply(state.Original, P.Root, token).Get<CommitReceipt>();
        var healthy = state.Apply(state.Healthy, P.Writer, token).Get<CommitReceipt>();
        await EqualAsync(state.OriginalReceipt, original);
        await EqualAsync(state.HealthyReceipt, healthy);
        await Assert.That(original.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
        await Assert.That(healthy.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
        await FailureAsync(state.Apply(state.Denied, P.Writer, token), P.ChangedPolicy);
        await FailureAsync(state.Apply(state.ReaderDenied, P.Reader, token), P.Denied);
        foreach (var retained in new[] { (state.Original, P.Root), (state.Denied, P.Writer),
            (state.Healthy, P.Writer), (state.ReaderDenied, P.Reader) })
        {
            await Assert.That(state.Outcome(retained.Item1, retained.Item2).AsSpan()
                .SequenceEqual(state.Outcomes[retained.Item1.CommandId])).IsTrue();
        }
        await UnchangedAsync(state, image, position);
    }
}

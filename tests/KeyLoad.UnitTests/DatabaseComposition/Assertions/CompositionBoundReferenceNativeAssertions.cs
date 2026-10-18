using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.Authorization;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal static class CompositionBoundReferenceNativeAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Length).IsEqualTo(expected.Mutations.Length);
        for (var index = 0; index < expected.Mutations.Length; index++)
        {
            await Assert.That(actual.Mutations[index].CompositionReferences.SequenceEqual(
                expected.Mutations[index].CompositionReferences)).IsTrue();
        }
    }

    internal static async Task OriginalAsync(SafeProjectorOperationDatabase owner, CompositionBoundReferenceNativeState state,
        CompositionBoundReferenceNativeCommand command)
    {
        var retained = owner.Entry(command.Operation).PreparedCommand!;
        await Assert.That(retained.CommandId).IsEqualTo(command.Operation.CommandId);
        await Assert.That(retained.Value!.Input.Span.SequenceEqual(command.Operation.Value!.Input.Span)).IsTrue();
        await Assert.That(DatabaseCommandFingerprint.Compute(retained)).IsEqualTo(DatabaseCommandFingerprint.Compute(command.Operation));
        var request = NativeSerialization.Deserialize<CommandRequest>(retained.Value!.Input.Span);
        await EqualAsync(command.Request, request);
        if (!command.Derived)
        {
            await Assert.That(((EnqueueMessage)request.Mutations.Last()).PayloadJson).IsEqualTo(state.CallerPayload);
        }
        var outcome = OutcomeStoreOracle.ReadStored(owner.Store, command.Operation)!;
        await ReceiptAsync(command.Receipt, outcome.Result.Get<CommitReceipt>());
        if (command.Derived)
        {
            await Assert.That(outcome.CompositionAuthority).IsNotNull();
            await Assert.That(outcome.CompositionAuthority!.References.SequenceEqual(command.AuthorityReferences)).IsTrue();
        }
        else
        {
            await Assert.That(outcome.CompositionAuthority).IsNull();
        }
    }

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, Func<Task> operation)
    {
        var image = TeamRowPolicyAssertions.Image(owner.Store);
        var identity = NativeSerialization.Serialize(owner.Store.Identity);
        var position = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCut();
        await operation();
        await Assert.That(TeamRowPolicyAssertions.Image(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(owner.ReadCut()).IsEqualTo(journal);
    }
}

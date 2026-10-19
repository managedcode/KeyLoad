using KeyLoad.Core;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RecurringSagaSchemaReplay
{
    private const string MissingStored = "The original Saga schema outcome is absent.";
    private const string MissingOperation = "The original Saga schema operation is absent.";
    private const string UnsupportedResult = "The Saga schema outcome has an unexpected execution type.";

    internal static async Task AllAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
    {
        foreach (var command in state.History)
        {
            await RecurringSagaSchemaProjection.ObserveAsync(owner, state, async () =>
            {
                var actual = owner.Database.Apply(command.Operation);
                await ResultAsync(actual, command.Result);
                await EntryAsync(owner, command);
            });
            await RecurringSagaSchemaProjection.ObserveAsync(owner, state, async () =>
            {
                var actual = owner.Database.ResolveOutcome(command.Operation);
                await ResultAsync(actual, command.Result);
                await EntryAsync(owner, command);
            });
            await RecurringSagaSchemaProjection.ObserveAsync(owner, state, async () =>
            {
                var actual = owner.Submit(command.Operation);
                await ResultAsync(actual, command.Result);
                await EntryAsync(owner, command);
            });
        }
    }

    internal static async Task StoredAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand command)
    {
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, command);
        var global = command.Operation.Value!.Kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal;
        var key = global ? KeySpace.GlobalOutcome(command.Operation.UserId!, command.Operation.CommandId)
            : KeySpace.PartitionOutcome(owner.Source.Partition, command.Operation.UserId!, command.Operation.CommandId);
        var stored = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key))
            ?? throw new InvalidOperationException(MissingStored);
        await Assert.That(stored.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(command.Operation));
        await Assert.That(stored.PolicyEpoch).IsEqualTo(command.CreatorEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(command.ExpectedToken.Incarnation);
        await Assert.That(stored.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await Assert.That(stored.CompositionAuthority is null).IsTrue();
        await Assert.That(stored.OnlineTextAuthority is null).IsTrue();
        await Assert.That(stored.RemoteTransferFailureAuthority is null).IsTrue();
        await ResultAsync(stored.Result, command.Result);
    }

    private static async Task EntryAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand command)
    {
        var entry = owner.ReadOutcome(command.Operation);
        await SafeProjectorOperationSeed.EqualAsync(entry, command.Outcome);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(MissingOperation);
        await Assert.That(operation.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(operation.Value!.Input.Span.SequenceEqual(command.Operation.Value!.Input.Span)).IsTrue();
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, command);
    }

    private static async Task ResultAsync(Result<object> actual, Result<object> expected)
    {
        switch (expected.Value)
        {
            case CommitReceipt:
                await ReplayResultAssertions.Same<CommitReceipt>(actual, expected);
                await RecurringSagaSchemaCanonical.PrivateReferencesAsync(actual.Get<CommitReceipt>());
                await RecurringSagaSchemaCanonical.PrivateReferencesAsync(expected.Get<CommitReceipt>());
                break;
            case PrincipalRecord:
                await ReplayResultAssertions.Same<PrincipalRecord>(actual, expected);
                break;
            case ResourceDefinition:
                await ReplayResultAssertions.Same<ResourceDefinition>(actual, expected);
                break;
            default:
                throw new InvalidOperationException(UnsupportedResult);
        }
    }
}

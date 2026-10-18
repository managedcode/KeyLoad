using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationReplay
{
    private const string ChangedPolicy = "The principal policy changed since this command was evaluated.";
    private const string MissingStored = "The original safe projector outcome is absent.";
    private const string Unsupported = "The safe projector history contains an unsupported original operation.";
    private const string UnsupportedResult = "The original safe projector result type is unsupported.";

    internal static async Task AllAsync(SafeProjectorOperationDatabase owner,
        IReadOnlyList<SafeProjectorOperationCommand> history, long currentEpoch)
    {
        foreach (var command in history)
        {
            await StoredAsync(owner, command);
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                var actual = owner.Database.Apply(command.Operation);
                await ResultAsync(actual, command.Result);
                await EntryAsync(owner, command);
            });
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                await FreshAsync(owner.Database.ResolveOutcome(command.Operation), command, currentEpoch);
                await EntryAsync(owner, command);
            });
            await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
            {
                await FreshAsync(owner.Submit(command.Operation), command, currentEpoch);
                await EntryAsync(owner, command);
            });
            await StoredAsync(owner, command);
        }
    }

    private static async Task FreshAsync(Result<object> actual, SafeProjectorOperationCommand command, long currentEpoch)
    {
        if (command.Operation.UserId! == SafeProjectorOperationProtocol.Reader && command.CreatorEpoch != currentEpoch)
        {
            await SafeProjectorOperationMutations.RefusalAsync(actual, ChangedPolicy);
            return;
        }
        await ResultAsync(actual, command.Result);
    }

    private static async Task EntryAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand expected)
    {
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, expected);
        var actual = owner.ReadOutcome(expected.Operation);
        await SafeProjectorOperationSeed.EqualAsync(actual, expected.Outcome);
        await Assert.That(actual.Fingerprint).IsEqualTo(DatabaseCommandFingerprint.Compute(expected.Operation));
        await Assert.That(actual.CanonicalSequence).IsEqualTo(expected.ExpectedToken.Position);
    }

    private static async Task StoredAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand command)
    {
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, command);
        var global = command.Operation.Value!.Kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal;
        if (!global && command.Operation.Value!.Kind != OperationKind.Batch)
        {
            throw new InvalidOperationException(Unsupported);
        }
        var key = global ? KeySpace.GlobalOutcome(command.Operation.UserId!, command.Operation.CommandId)
            : KeySpace.PartitionOutcome(owner.Source.Partition, command.Operation.UserId!, command.Operation.CommandId);
        var actual = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key)) ?? throw new InvalidOperationException(MissingStored);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Fingerprint, DatabaseCommandFingerprint.Compute(command.Operation))).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(command.CreatorEpoch);
        await Assert.That(actual.Incarnation).IsEqualTo(command.ExpectedToken.Incarnation);
        await Assert.That(actual.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(actual.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await Assert.That(actual.BlobAuthority is null).IsTrue();
        await Assert.That(actual.CompositionAuthority is null).IsTrue();
        await Assert.That(actual.OnlineTextAuthority is null).IsTrue();
        await Assert.That(actual.RemoteTransferFailureAuthority is null).IsTrue();
        await ResultAsync(actual.Result, command.Result);
    }

    private static async Task ResultAsync(Result<object> actual, Result<object> expected)
    {
        if (Errors.Code(expected.Problem) is not null)
        {
            await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(expected.Problem));
            await Assert.That(StringComparer.Ordinal.Equals(actual.Problem?.Detail, expected.Problem?.Detail)).IsTrue();
            await Assert.That(StringComparer.Ordinal.Equals(actual.Json, expected.Json)).IsTrue();
            await Assert.That(actual.Value is null).IsTrue();
            return;
        }
        switch (expected.Value)
        {
            case CommitReceipt:
                await SuccessAsync<CommitReceipt>(actual, expected);
                break;
            case PrincipalRecord:
                await SuccessAsync<PrincipalRecord>(actual, expected);
                break;
            case ResourceDefinition:
                await SuccessAsync<ResourceDefinition>(actual, expected);
                break;
            default:
                throw new InvalidOperationException(UnsupportedResult);
        }
    }

    private static async Task SuccessAsync<T>(Result<object> actual, Result<object> expected) where T : class
    {
        await Assert.That(StringComparer.Ordinal.Equals(actual.Json, expected.Json)).IsTrue();
        await Assert.That(Errors.Code(actual.Problem) == Errors.Code(expected.Problem)).IsTrue();
        await Assert.That(StringComparer.Ordinal.Equals(actual.Problem?.Detail, expected.Problem?.Detail)).IsTrue();
        await Assert.That(actual.Value?.GetType() == typeof(T)).IsTrue();
        await Assert.That(expected.Value?.GetType() == typeof(T)).IsTrue();
        await SafeProjectorOperationSeed.EqualAsync(actual.Get<T>(), expected.Get<T>());
    }
}

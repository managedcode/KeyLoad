using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

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
                var actual = owner.Database.Apply(command.Operation, command.Entry.Index);
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

    private static async Task FreshAsync(OperationResult actual, SafeProjectorOperationCommand command, long currentEpoch)
    {
        if (command.Operation.PrincipalId == SafeProjectorOperationProtocol.Reader && command.CreatorEpoch != currentEpoch)
        {
            await SafeProjectorOperationMutations.RefusalAsync(actual, ChangedPolicy);
            return;
        }
        await ResultAsync(actual, command.Result);
    }

    private static async Task EntryAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand expected)
    {
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, expected);
        var actual = owner.Entry(expected.Operation);
        await SafeProjectorOperationSeed.EqualAsync(actual, expected.Entry);
        var operation = actual.Operation ?? throw new InvalidOperationException(Unsupported);
        await Assert.That(operation.NativePayload.IsEmpty).IsFalse();
        await Assert.That(operation.NativePayload.Span.SequenceEqual(expected.Operation.NativePayload.Span)).IsTrue();
    }

    private static async Task StoredAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand command)
    {
        await SafeProjectorOperationHistoryImages.UnchangedAsync(owner, command);
        var global = command.Operation.Kind is OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal;
        if (!global && command.Operation.Kind != OperationKind.Batch)
        {
            throw new InvalidOperationException(Unsupported);
        }
        var key = global ? KeySpace.GlobalOutcome(command.Operation.PrincipalId, command.Operation.Id)
            : KeySpace.PartitionOutcome(owner.Source.Partition, command.Operation.PrincipalId, command.Operation.Id);
        var actual = owner.Store.Read(view => view.GetRecord<StoredOutcome>(key)) ?? throw new InvalidOperationException(MissingStored);
        await Assert.That(StringComparer.Ordinal.Equals(actual.Fingerprint, NativeOperationFingerprint.Compute(command.Operation))).IsTrue();
        await Assert.That(actual.PolicyEpoch).IsEqualTo(command.CreatorEpoch);
        await Assert.That(actual.Incarnation).IsEqualTo(command.ExpectedToken.Incarnation);
        await Assert.That(actual.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(actual.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await Assert.That(actual.BlobAuthority is null).IsTrue();
        await Assert.That(actual.CompositionAuthority is null).IsTrue();
        await Assert.That(actual.OnlineTextAuthority is null).IsTrue();
        await Assert.That(actual.RemoteTransferAuthority is null).IsTrue();
        await Assert.That(actual.RemoteTransferFailureAuthority is null).IsTrue();
        await ResultAsync(actual.Result, command.Result);
    }

    private static async Task ResultAsync(OperationResult actual, OperationResult expected)
    {
        if (expected.Error is not null)
        {
            await Assert.That(actual.Error).IsEqualTo(expected.Error);
            await Assert.That(StringComparer.Ordinal.Equals(actual.SafeDetail, expected.SafeDetail)).IsTrue();
            await Assert.That(StringComparer.Ordinal.Equals(actual.Json, expected.Json)).IsTrue();
            await Assert.That(actual.NativeValue is null).IsTrue();
            return;
        }
        switch (expected.NativeValue)
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

    private static async Task SuccessAsync<T>(OperationResult actual, OperationResult expected) where T : class
    {
        await Assert.That(StringComparer.Ordinal.Equals(actual.Json, expected.Json)).IsTrue();
        await Assert.That(actual.Error == expected.Error).IsTrue();
        await Assert.That(StringComparer.Ordinal.Equals(actual.SafeDetail, expected.SafeDetail)).IsTrue();
        await Assert.That(actual.NativeValue?.GetType() == typeof(T)).IsTrue();
        await Assert.That(expected.NativeValue?.GetType() == typeof(T)).IsTrue();
        await SafeProjectorOperationSeed.EqualAsync(actual.Get<T>(), expected.Get<T>());
    }
}

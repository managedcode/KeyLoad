using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationHistoryAssertions
{
    internal static async Task SaveAsync<T>(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        Command<DatabaseCommand> issued, Result<object> result, T expected) where T : class
    {
        await NativeReplayResultAssertions.Same<T>(result, Result<object>.Succeed(expected));
        var entry = owner.ReadStoredOutcome(issued.CommandId);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(MissingOriginal);
        var global = operation.Value!.Kind == OperationKind.ConfigureResource;
        var key = global ? KeySpace.GlobalOutcome(Root, operation.CommandId)
            : KeySpace.PartitionOutcome(owner.Source.Partition, Root, operation.CommandId);
        var locatorKey = KeySpace.OutcomeLocatorV2(owner.Source.Partition, Root, operation.CommandId);
        var outcome = Required(owner, key);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(locatorKey));
        await LocatorAsync(locator, global ? null : key);
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(stored.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await NativeReplayResultAssertions.Same<T>(stored.Result, result);
        state.History.Add(new(operation, result, entry.CanonicalSequence, operation.Value!.Input.ToArray(),
            key, outcome, locatorKey, locator));
    }

    internal static async Task AllAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        foreach (var history in state.History)
        {
            token.ThrowIfCancellationRequested();
            var image = AnnProviderQualificationImages.Capture(owner);
            await ResultAsync(owner.Submit(history.Operation, explicitTime: true, token), history.Result);
            await AnnProviderQualificationImages.SameAsync(owner, image);
            await RowsAsync(owner, history);
            await ResultAsync(owner.Database.ResolveOutcome(history.Operation), history.Result);
            await AnnProviderQualificationImages.SameAsync(owner, image);
            await RowsAsync(owner, history);
        }
    }

    private static async Task ResultAsync(Result<object> actual, Result<object> expected)
    {
        if (expected.Value is ResourceDefinition)
        {
            await NativeReplayResultAssertions.Same<ResourceDefinition>(actual, expected);
        }
        else
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(actual, expected);
        }
    }

    private static async Task RowsAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationHistory history)
    {
        await Assert.That(Required(owner, history.OutcomeKey).AsSpan().SequenceEqual(history.OutcomeBytes)).IsTrue();
        await LocatorAsync(owner.Store.Read(view => view.ReadOwnedValue(history.LocatorKey)), history.LocatorBytes);
        var entry = owner.ReadStoredOutcome(history.Operation);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(history.Sequence);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(MissingOriginal);
        await Assert.That(operation.CommandId).IsEqualTo(history.Operation.CommandId);
        await Assert.That(operation.Value!.Kind).IsEqualTo(history.Operation.Value!.Kind);
        await Assert.That(operation.UserId!).IsEqualTo(history.Operation.UserId!);
        await Assert.That(new DateTimeOffset(operation.Timestamp)).IsEqualTo(new DateTimeOffset(history.Operation.Timestamp));
        await Assert.That(operation.Value!.Fingerprint).IsEqualTo(history.Operation.Value!.Fingerprint);
        await Assert.That(operation.Value!.Input.Span.SequenceEqual(history.Payload)).IsTrue();
    }

    private static async Task LocatorAsync(byte[]? actual, byte[]? expected)
    {
        if (expected is null)
        {
            await Assert.That(actual).IsNull();
            return;
        }
        var present = actual ?? throw new InvalidOperationException(MissingOriginal);
        await Assert.That(present.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static byte[] Required(AnnProviderQualificationOwner owner, byte[] key)
        => owner.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingOriginal);
}

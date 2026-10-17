using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationHistoryAssertions
{
    internal static async Task SaveAsync<T>(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        ReplicatedOperation issued, OperationResult result, T expected) where T : class
    {
        await NativeReplayResultAssertions.Same<T>(result, new(null)
        {
            NativeValue = expected
        });
        var entry = owner.Native.ReadRetainedEntry(issued.Id);
        var operation = entry.Operation ?? throw new InvalidOperationException(MissingOriginal);
        var global = operation.Kind == OperationKind.ConfigureResource;
        var key = global ? KeySpace.GlobalOutcome(Root, operation.Id)
            : KeySpace.PartitionOutcome(owner.Source.Partition, Root, operation.Id);
        var locatorKey = KeySpace.OutcomeLocatorV2(owner.Source.Partition, Root, operation.Id);
        var outcome = Required(owner, key);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(locatorKey));
        await LocatorAsync(locator, global ? null : key);
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(PolicyEpoch);
        await Assert.That(stored.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await NativeReplayResultAssertions.Same<T>(stored.Result, result);
        state.History.Add(new(operation, result, entry.Index, entry.Term, operation.NativePayload.ToArray(),
            key, outcome, locatorKey, locator));
    }

    internal static async Task AllAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        CancellationToken token)
    {
        foreach (var history in state.History)
        {
            token.ThrowIfCancellationRequested();
            var image = AnnProviderQualificationImages.Capture(owner);
            await ResultAsync(owner.Native.Submit(history.Operation, explicitTime: true, token), history.Result);
            await AnnProviderQualificationImages.SameAsync(owner, image);
            await RowsAsync(owner, history);
            await ResultAsync(owner.Database.ResolveOutcome(history.Operation), history.Result);
            await AnnProviderQualificationImages.SameAsync(owner, image);
            await RowsAsync(owner, history);
        }
    }

    private static async Task ResultAsync(OperationResult actual, OperationResult expected)
    {
        if (expected.NativeValue is ResourceDefinition)
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
        var entry = owner.Native.ReadRetainedEntry(history.Operation);
        await Assert.That(entry.Index).IsEqualTo(history.Index);
        await Assert.That(entry.Term).IsEqualTo(history.Term);
        var operation = entry.Operation ?? throw new InvalidOperationException(MissingOriginal);
        await Assert.That(operation.Id).IsEqualTo(history.Operation.Id);
        await Assert.That(operation.Kind).IsEqualTo(history.Operation.Kind);
        await Assert.That(operation.PrincipalId).IsEqualTo(history.Operation.PrincipalId);
        await Assert.That(operation.EvaluatedAt).IsEqualTo(history.Operation.EvaluatedAt);
        await Assert.That(operation.PayloadJson).IsEqualTo(history.Operation.PayloadJson);
        await Assert.That(operation.NativePayload.Span.SequenceEqual(history.Payload)).IsTrue();
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

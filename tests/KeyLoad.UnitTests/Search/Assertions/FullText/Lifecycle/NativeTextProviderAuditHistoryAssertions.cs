using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.NativeTextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextProviderAuditHistoryAssertions
{
    internal static async Task SaveAsync<T>(NativeTextProtectedRebuildColdOwner owner, NativeTextProviderAuditState state,
        NativeTextProtectedRebuildColdOwner.Retained original, T expected) where T : class
    {
        await NativeReplayResultAssertions.Same<T>(original.Result, Result<object>.Succeed(expected));
        var global = original.Operation.Value!.Kind == OperationKind.ConfigureResource;
        var outcomeKey = global ? KeySpace.GlobalOutcome(Root, original.Operation.CommandId)
            : KeySpace.PartitionOutcome(owner.Source.Partition, Root, original.Operation.CommandId);
        var locatorKey = KeySpace.OutcomeLocatorV2(owner.Source.Partition, Root, original.Operation.CommandId);
        var outcome = Required(owner, outcomeKey);
        var locator = owner.Store.Read(view => view.ReadOwnedValue(locatorKey));
        if (global)
        {
            await Assert.That(locator).IsNull();
        }
        else
        {
            await Assert.That(locator).IsNotNull();
            var present = locator ?? throw new InvalidOperationException(MissingOriginal);
            await Assert.That(present.AsSpan().SequenceEqual(outcomeKey)).IsTrue();
        }
        var stored = NativeSerialization.Deserialize<StoredOutcome>(outcome);
        await Assert.That(stored.Incarnation).IsEqualTo(owner.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        await Assert.That(stored.ScopeKind).IsEqualTo(global ? CommandOutcomeScopeKind.Global : CommandOutcomeScopeKind.Partition);
        await Assert.That(stored.Partition).IsEqualTo(global ? null : owner.Source.Partition);
        await NativeReplayResultAssertions.Same<T>(stored.Result, original.Result);
        state.History.Add(new(original, outcomeKey, outcome, locatorKey, locator));
    }

    internal static async Task AllAsync(NativeTextProtectedRebuildColdOwner owner, NativeTextProviderAuditState state,
        CancellationToken token)
    {
        foreach (var history in state.History)
        {
            token.ThrowIfCancellationRequested();
            var image = NativeTextProviderAuditImages.Capture(owner);
            await ResultAsync(owner.Database.ApplyEmbedded(history.Original.Operation, cancellationToken: token), history.Original.Result);
            await RowsAsync(owner, history);
            await NativeTextProviderAuditImages.SameAsync(owner, image);
            await ResultAsync(owner.Database.ResolveOutcome(history.Original.Operation), history.Original.Result);
            await RowsAsync(owner, history);
            await NativeTextProviderAuditImages.SameAsync(owner, image);
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

    private static async Task RowsAsync(NativeTextProtectedRebuildColdOwner owner, NativeTextProviderAuditHistory history)
    {
        await Assert.That(Required(owner, history.OutcomeKey).AsSpan().SequenceEqual(history.OutcomeBytes)).IsTrue();
        var locator = owner.Store.Read(view => view.ReadOwnedValue(history.LocatorKey));
        if (history.LocatorBytes is { } expected)
        {
            await Assert.That(locator).IsNotNull();
            var present = locator ?? throw new InvalidOperationException(MissingOriginal);
            await Assert.That(present.AsSpan().SequenceEqual(expected)).IsTrue();
        }
        else
        {
            await Assert.That(locator).IsNull();
        }
    }

    private static byte[] Required(NativeTextProtectedRebuildColdOwner owner, byte[] key)
        => owner.Store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(MissingOriginal);
}

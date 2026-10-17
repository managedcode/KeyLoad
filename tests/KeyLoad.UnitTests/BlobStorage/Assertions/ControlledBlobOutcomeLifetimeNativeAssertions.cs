using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeAssertions
{
    private const int SixKinds = 6;

    internal static async Task InventoryAsync(ControlledBlobOutcomeLifetimeNativeState state)
    {
        await Assert.That(state.Proofs.Length).IsEqualTo(SixKinds);
        foreach (var kind in ControlledBlobOutcomeLifetimeNativeCommands.Kinds)
        {
            await Assert.That(state.Proofs.Count(value => value.Original.Kind == kind)).IsEqualTo(1);
        }
        await Assert.That(state.Source.Database.Limits.MaxBatchBytes)
            .IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
        await Assert.That(state.Target.Database.Limits.MaxBatchBytes)
            .IsEqualTo(NativeControlledLifetimePayloadBoundary.MaximumBytes);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static async Task UnchangedAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledPartitionMovementExpiryOwnerState source, ControlledPartitionMovementExpiryOwnerState target)
    {
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
    }

    internal static Dictionary<string, byte[]> OriginalRows(ControlledBlobOutcomeLifetimeNativeState state)
        => state.Source.Store.Read(view => state.Proofs.SelectMany(proof =>
        {
            var identity = new PartitionControlCommandIdentity(CommandOutcomeScopeKind.Partition,
                ControlledPartitionMovementCorpus.Partition, proof.Original.PrincipalId, proof.Original.Id);
            return new[] { PartitionControlCommandKeys.Original(identity), PartitionControlCommandKeys.Authority(identity) };
        }).ToDictionary(Convert.ToHexString, key => view.ReadOwnedValue(key)
            ?? throw new InvalidOperationException("The original native Blob outcome authority is absent."), StringComparer.Ordinal));

    internal static async Task OriginalRowsUnchangedAsync(ControlledBlobOutcomeLifetimeNativeState state,
        Dictionary<string, byte[]> original)
    {
        var current = OriginalRows(state);
        await Assert.That(current.Count).IsEqualTo(original.Count);
        foreach (var row in original)
        {
            await Assert.That(current[row.Key].AsSpan().SequenceEqual(row.Value)).IsTrue();
        }
    }
}

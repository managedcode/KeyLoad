using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobOriginalOutcomeRf3Assertions
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after)
    {
        var originals = seed.BlobOriginals;
        await RequireOneAsync(seed, before, after, originals.Begin.CommandId, originals.Begun);
        await RequireOneAsync(seed, before, after, originals.First.CommandId, originals.FirstReceipt);
        await RequireOneAsync(seed, before, after, originals.Tail.CommandId, originals.TailReceipt);
        await RequireOneAsync(seed, before, after, originals.Complete.CommandId, originals.Completed);
    }

    private static async Task RequireOneAsync<T>(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementPublicParentRf3NativeCut before, PartitionMovementPublicParentRf3NativeCut after,
        Guid commandId, T expected)
    {
        var key = KeySpace.PartitionOutcome(seed.Partition,
            PartitionMovementPublicParentRf3Administrator.PrincipalId, commandId);
        var original = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(before, key);
        var actual = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(after, key);
        await Assert.That(original.PolicyEpoch).IsEqualTo(
            PartitionMovementPublicParentRf3Administrator.InitialDefinition().PolicyEpoch);
        await Assert.That(original.Incarnation).IsEqualTo(seed.Directory.ControlOwner.Incarnation);
        await Assert.That(original.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Partition);
        await SqlRf3Protocol.EqualAsync(seed.Partition, original.Partition);
        await Assert.That(original.Result.Error).IsNull();
        await SqlRf3Protocol.EqualAsync(expected, original.Result.Get<T>());
        await Assert.That(original.BlobAuthority).IsNotNull();
        await Assert.That(original.BlobAuthority!.BeginCommandId).IsEqualTo(seed.BlobOriginals.Begin.CommandId);
        await Assert.That(original.BlobAuthority.CreatorPrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        var rowKey = Convert.ToHexString(key);
        var beforeRow = PartitionMovementCapturePointerRf3Fault.Rows(before)[rowKey];
        var afterRow = PartitionMovementCapturePointerRf3Fault.Rows(after)[rowKey];
        await Assert.That(afterRow).IsEqualTo(beforeRow);
        await SqlRf3Protocol.EqualAsync(original, actual);
    }
}

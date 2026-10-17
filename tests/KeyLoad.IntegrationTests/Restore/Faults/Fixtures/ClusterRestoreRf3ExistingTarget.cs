using System.Collections.Immutable;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3ExistingTarget
{
    private const int FirstNode = 0;
    private const int GroupMembers = 3;

    internal static async Task RetainAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals)
    {
        var receipt = await target.ReadOriginalReceiptAsync().ConfigureAwait(false);
        if (receipt.CaptureId != originals.First().Cut.CaptureId
            || receipt.Nodes.Length != ClusterRestoreRf3Protocol.Nodes.Length)
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        var ids = new HashSet<Guid>();
        for (var index = FirstNode; index < receipt.Nodes.Length; index++)
        {
            var node = receipt.Nodes[index];
            var mapping = target.Mappings[index / GroupMembers];
            if (node.SourceOwnerId != mapping.Source.PhysicalShardId
                || node.TargetOwnerId != mapping.Target.PhysicalShardId
                || !string.Equals(node.VoterId, mapping.Target.VoterIds[index % GroupMembers], StringComparison.Ordinal)
                || !string.Equals(node.RelativeDataDirectory, ClusterRestoreRf3Protocol.Nodes[index], StringComparison.Ordinal)
                || node.Incarnation != mapping.Target.Incarnation || !node.DispatchPaused
                || node.NodeId == Guid.Empty || !ids.Add(node.NodeId)
                || originals.Any(original => original.Cut.SourceNodeId == node.NodeId))
            {
                throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
            }
        }
        if (!target.OriginalTargetNodes.IsDefaultOrEmpty
            && !target.OriginalTargetNodes.SequenceEqual(receipt.Nodes))
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        target.OriginalTargetNodes = receipt.Nodes;
    }

    internal static string DirectoryPath(ClusterRestoreRf3Fixture target, int index)
        => Path.Combine(target.DataRoot, ClusterRestoreRf3Protocol.Nodes[index],
            ClusterRestoreRf3Protocol.DatabaseDirectory);

    internal static ZoneTreeStore Open(ClusterRestoreRf3Fixture target, int index)
    {
        if (target.OriginalTargetNodes.Length != ClusterRestoreRf3Protocol.Nodes.Length)
        {
            throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        }
        var node = target.OriginalTargetNodes[index];
        var mapping = target.Mappings[index / GroupMembers];
        return ZoneTreeExistingStore.Open(new(DirectoryPath(target, index))
        {
            Incarnation = mapping.Target.Incarnation
        }, node.NodeId, IntegrationExecutionOptions.StorageExecution());
    }
}

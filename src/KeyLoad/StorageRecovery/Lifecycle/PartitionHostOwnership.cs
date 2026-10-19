using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server;

/// <summary>Actual settlement of one node-local host acquisition and its original physical owners.</summary>
internal sealed class PartitionHostOwnership
{
    private bool nodeOwnershipReleased = true;
    internal NativeStorageOwnerSettlement Canonical { get; } = new();
    internal NativeStorageOwnerSettlement Host { get; } = new();
    internal bool CanReleaseRoot => nodeOwnershipReleased && Canonical.IsReleased
        && Host.IsReleased;
    internal void NodeOwnershipAcquired() => nodeOwnershipReleased = false;
    internal void NodeOwnershipReleased() => nodeOwnershipReleased = true;
}

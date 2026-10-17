using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ArchiveSlot(
    D2ArchiveCapture Archive,
    ImmutableArray<ClusterBackupOwnerCut> Cuts,
    ImmutableArray<ClusterRestoreOwnerMapping> Mappings,
    ClusterRestoreOwnerMapping Mapping,
    ClusterRestoreSlotContext Context,
    ReadOnlyMemory<byte> SigningKey,
    string Destination)
{
    internal string Stage => Destination + D2ArchiveData.StageSuffix;
}

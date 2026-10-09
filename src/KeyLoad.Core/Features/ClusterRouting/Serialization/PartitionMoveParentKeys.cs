using KeyLoad.Storage;
namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Control-owned first-release metadata remains outside transferred model record families.</summary>
internal static class PartitionMoveParentKeys
{
    private const string HeaderSpace = "partition-move-parent-v1";
    private const string PhaseSpace = "partition-move-parent-phase-v1";
    private const string QuotaSpace = "partition-move-parent-quota-v1";
    private const string ActiveMarker = "active";
    private const string PrincipalActiveMarker = "principal-active";
    private const string DatabaseActiveMarker = "database-active";
    private const string DatabaseTerminalCountMarker = "database-terminal-count";
    private const string DatabaseTerminalBytesMarker = "database-terminal-bytes";
    internal static byte[] Active(PartitionRef partition)
        => KeySpace.Partition(HeaderSpace, partition, ActiveMarker);
    internal static byte[] Header(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(HeaderSpace, partition, moveId);
    internal static byte[] PhasePrefix(PartitionRef partition, Guid moveId)
        => KeySpace.Partition(PhaseSpace, partition, moveId);
    internal static byte[] Phase(PartitionRef partition, Guid moveId, Guid originalPhaseId)
        => KeySpace.Partition(PhaseSpace, partition, moveId, originalPhaseId);
    internal static byte[] PrincipalActive(string principalId)
        => KeyCodec.Encode(QuotaSpace, PrincipalActiveMarker, principalId);
    internal static byte[] DatabaseActive(PartitionRef partition)
        => KeyCodec.Encode(QuotaSpace, DatabaseActiveMarker, partition.TenantId, partition.DatabaseId);
    internal static byte[] DatabaseTerminalCount(PartitionRef partition)
        => KeyCodec.Encode(QuotaSpace, DatabaseTerminalCountMarker, partition.TenantId, partition.DatabaseId);
    internal static byte[] DatabaseTerminalBytes(PartitionRef partition)
        => KeyCodec.Encode(QuotaSpace, DatabaseTerminalBytesMarker, partition.TenantId, partition.DatabaseId);
}

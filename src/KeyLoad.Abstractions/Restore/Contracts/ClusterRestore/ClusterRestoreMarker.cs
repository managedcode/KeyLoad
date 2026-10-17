using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Retains original verified cut evidence and explicit new-generation configuration, never a trusted role.</summary>
/// <param name="Version">Independent marker schema.</param>
/// <param name="OriginalCut">Complete original captured owner cut.</param>
/// <param name="Mappings">Complete explicitly admitted configured mapping.</param>
/// <param name="SlotContext">Original native operation slot, required only by the new resumable owner.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreMarker.SerializerAlias)]
public sealed record ClusterRestoreMarker(
    [property: Orleans.Id(ClusterRestoreMarker.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreMarker.CutField)] ClusterBackupOwnerCut OriginalCut,
    [property: Orleans.Id(ClusterRestoreMarker.MappingsField)] ImmutableArray<ClusterRestoreOwnerMapping> Mappings,
    [property: Orleans.Id(ClusterRestoreMarker.SlotField)] ClusterRestoreSlotContext? SlotContext = null)
{
    internal const string SerializerAlias = "keyload.backup.cluster-restore-marker.v1";
    /// <summary>Current marker schema version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int CutField = 1;
    private const int MappingsField = 2;
    private const int SlotField = 3;
}

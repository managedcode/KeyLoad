using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Nonsecret observed offline publication receipt; it is not an RF3 durability receipt.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreExecutionReceipt.SerializerAlias)]
internal sealed record ClusterRestoreExecutionReceipt([property: Orleans.Id(ClusterRestoreExecutionReceipt.CaptureField)] Guid CaptureId,
    [property: Orleans.Id(ClusterRestoreExecutionReceipt.NodesField)] ImmutableArray<ClusterRestoreNodeReceipt> Nodes,
    [property: Orleans.Id(ClusterRestoreExecutionReceipt.ElapsedField)] TimeSpan ActualElapsed)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.execution-receipt.v1";
    private const int CaptureField = 0;
    private const int NodesField = 1;
    private const int ElapsedField = 2;
}

/// <summary>Observed native identity after its actual store and readers have joined.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreNodeReceipt.SerializerAlias)]
internal sealed record ClusterRestoreNodeReceipt([property: Orleans.Id(ClusterRestoreNodeReceipt.SourceField)] Guid SourceOwnerId,
    [property: Orleans.Id(ClusterRestoreNodeReceipt.TargetField)] Guid TargetOwnerId, [property: Orleans.Id(ClusterRestoreNodeReceipt.VoterField)] string VoterId,
    [property: Orleans.Id(ClusterRestoreNodeReceipt.PathField)] string RelativeDataDirectory, [property: Orleans.Id(ClusterRestoreNodeReceipt.NodeField)] Guid NodeId,
    [property: Orleans.Id(ClusterRestoreNodeReceipt.IncarnationField)] Guid Incarnation, [property: Orleans.Id(ClusterRestoreNodeReceipt.PausedField)] bool DispatchPaused)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.node-receipt.v1";
    private const int SourceField = 0;
    private const int TargetField = 1;
    private const int VoterField = 2;
    private const int PathField = 3;
    private const int NodeField = 4;
    private const int IncarnationField = 5;
    private const int PausedField = 6;
}

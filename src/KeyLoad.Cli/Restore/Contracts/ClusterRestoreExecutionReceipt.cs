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

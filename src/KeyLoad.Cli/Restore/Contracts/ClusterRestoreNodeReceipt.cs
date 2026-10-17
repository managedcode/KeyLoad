namespace KeyLoad.Cli.Features.BackupRestore;

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

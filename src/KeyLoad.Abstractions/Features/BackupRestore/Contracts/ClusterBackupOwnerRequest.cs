namespace KeyLoad;

/// <summary>Requests a bounded stable capture on one actual configured native owner.</summary>
/// <param name="Version">Current request format.</param>
/// <param name="CaptureId">Stable capture identity retained by the immutable native owner archive.</param>
/// <param name="ExpectedNodeId">Exact actual native source node observed through authenticated status.</param>
/// <param name="ExpectedOwner">Expected physical tuple, compared with actual server configuration; it grants no authority.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterBackupOwnerRequest.SerializerAlias)]
public sealed record ClusterBackupOwnerRequest(
    [property: Orleans.Id(ClusterBackupOwnerRequest.VersionField)] int Version,
    [property: Orleans.Id(ClusterBackupOwnerRequest.CaptureField)] Guid CaptureId,
    [property: Orleans.Id(ClusterBackupOwnerRequest.OwnerField)] PhysicalShardRecord ExpectedOwner,
    [property: Orleans.Id(ClusterBackupOwnerRequest.NodeField)] Guid ExpectedNodeId)
{
    internal const string SerializerAlias = "keyload.backup.cluster-owner-request.v1";
    /// <summary>The current request version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int CaptureField = 1;
    private const int OwnerField = 2;
    private const int NodeField = 3;
}

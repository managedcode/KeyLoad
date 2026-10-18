namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateAnnMaintenanceChild(Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, byte[] payload, DateTimeOffset expiry)
        => SearchMaintenanceRequestIssuance.CreateAnnMaintenanceChild(this, requestId, principalId, readKind, commandKind, commandId, payload, expiry);

    internal string CreateTextMaintenanceChild(Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, byte[] payload, DateTimeOffset expiry)
        => SearchMaintenanceRequestIssuance.CreateTextMaintenanceChild(this, requestId, principalId, readKind, commandKind, commandId, payload, expiry);
}

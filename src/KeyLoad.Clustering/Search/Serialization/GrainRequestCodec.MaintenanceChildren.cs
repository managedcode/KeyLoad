namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateMaintenanceChild(string purpose, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, object payload)
        => SearchMaintenanceRequestIssuance.CreateChild(this, purpose, requestId, principalId, readKind, commandKind,
            commandId, payload);
}

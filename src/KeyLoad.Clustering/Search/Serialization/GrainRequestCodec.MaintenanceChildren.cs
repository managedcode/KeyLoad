namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateMaintenanceChild<TPayload>(string purpose, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, TPayload payload) where TPayload : notnull
        => SearchMaintenanceRequestIssuance.CreateChild(this, purpose, requestId, principalId, readKind, commandKind,
            commandId, payload);
}

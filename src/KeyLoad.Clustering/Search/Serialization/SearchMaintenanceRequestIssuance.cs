namespace KeyLoad.Clustering;

/// <summary>Issues maintenance children under their parent's child purpose.</summary>
internal static class SearchMaintenanceRequestIssuance
{
    internal static GrainRequestEnvelope CreateChild(GrainRequestCodec codec, string purpose, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, object payload)
        => codec.Issue(new GrainRequestEnvelope
        {
            Purpose = purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = codec.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload)
        });
}

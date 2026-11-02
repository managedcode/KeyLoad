namespace KeyLoad.Clustering;

/// <summary>Issues maintenance children under their parent's child purpose.</summary>
internal static class SearchMaintenanceRequestIssuance
{
    internal static GrainRequestEnvelope CreateChild<TPayload>(GrainRequestCodec codec, string purpose, Guid requestId, string principalId,
        GrainReadKind? readKind, OperationKind? commandKind, Guid commandId, TPayload payload) where TPayload : notnull
        => codec.Issue(new GrainRequestEnvelope
        {
            Purpose = purpose,
            RequestId = requestId,
            PrincipalId = principalId,
            Incarnation = codec.Incarnation,
            ReadKind = readKind,
            CommandKind = commandKind,
            CommandId = commandId,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (GrainPayload.Copy(payload) /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        });
}


namespace KeyLoad.Clustering;

internal static class GrainRequestCapabilityEnvelopes
{
    internal static GrainRequestEnvelope RemoteRead<TPayload>(GrainRequestCodec scope, Guid requestId, string principalId,
        GrainReadKind kind, TPayload payload) where TPayload : notnull
        => new()
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (GrainPayload.Copy(payload) /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        };

    internal static GrainRequestEnvelope RuntimeJournalRead<TPayload>(GrainRequestCodec scope,
        Guid requestId, string principalId, GrainReadKind kind, TPayload payload) where TPayload : notnull
        => new()
        {
            Purpose = GrainContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (GrainPayload.Copy(payload) /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        };

    internal static GrainRequestEnvelope RuntimeJournalCommand<TPayload>(GrainRequestCodec scope,
        Guid requestId, string principalId, Guid commandId, TPayload payload) where TPayload : notnull
        => new()
        {
            Purpose = GrainContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            CommandKind = OperationKind.RuntimeJournal,
            CommandId = commandId,
            Payload /* KLD0041: Preserve the current generated Payload field ID and runtime type header at this request envelope boundary. */ = (GrainPayload.Copy(payload) /* KLD0042: The current generated envelope Payload field requires object; a scalar NoDto marker boxes here once, while request DTOs retain their native reference type headers. */)
        };
}

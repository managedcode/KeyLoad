
namespace KeyLoad.Clustering;

internal static class GrainRequestCapabilityEnvelopes
{
    internal static GrainRequestEnvelope RemoteRead(GrainRequestCodec scope, Guid requestId, string principalId,
        GrainReadKind kind, object payload)
        => new()
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainPayload.Copy(payload)
        };

    internal static GrainRequestEnvelope RuntimeJournalRead(GrainRequestCodec scope,
        Guid requestId, string principalId, GrainReadKind kind, object payload)
        => new()
        {
            Purpose = GrainContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainPayload.Copy(payload)
        };

    internal static GrainRequestEnvelope RuntimeJournalCommand(GrainRequestCodec scope,
        Guid requestId, string principalId, Guid commandId, object payload)
        => new()
        {
            Purpose = GrainContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            CommandKind = OperationKind.RuntimeJournal,
            CommandId = commandId,
            Payload = GrainPayload.Copy(payload)
        };
}

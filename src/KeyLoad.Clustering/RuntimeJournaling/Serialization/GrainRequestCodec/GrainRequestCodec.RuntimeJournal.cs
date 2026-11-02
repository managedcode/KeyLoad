using KeyLoad.Core.RuntimeJournaling.Identity;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateRuntimeJournalRead<TPayload>(Guid requestId, GrainReadKind kind, TPayload payload) where TPayload : notnull
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalRead(this,
            requestId, RuntimeJournalIdentity.ProtectedPrincipalId, kind, payload));

    internal GrainRequestEnvelope CreateRuntimeJournalCommand<TPayload>(Guid requestId, string principalId, Guid commandId,
        TPayload payload) where TPayload : notnull
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalCommand(this,
            requestId, principalId, commandId, payload));
}

using KeyLoad.Core.RuntimeJournaling.Identity;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateRuntimeJournalRead(Guid requestId, GrainReadKind kind, object payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalRead(this, clock,
            settings.RequestLifetime, requestId, RuntimeJournalIdentity.ProtectedPrincipalId, kind, payload));

    internal GrainRequestEnvelope CreateRuntimeJournalCommand(Guid requestId, string principalId, Guid commandId,
        object payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalCommand(this, clock,
            settings.RequestLifetime, requestId, principalId, commandId, payload));
}

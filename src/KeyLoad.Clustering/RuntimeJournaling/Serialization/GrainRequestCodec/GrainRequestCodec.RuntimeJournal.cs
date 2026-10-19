using KeyLoad.Core.Features.CommandOutcomes.Identity;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateRuntimeJournalRead(Guid requestId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalRead(this, clock,
            settings.RequestLifetime, requestId, RuntimeJournalIdentity.ProtectedPrincipalId, kind, payload));

    internal string CreateRuntimeJournalCommand(Guid requestId, string principalId, Guid commandId,
        ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalCommand(this, clock,
            settings.RequestLifetime, requestId, principalId, commandId, payload));
}

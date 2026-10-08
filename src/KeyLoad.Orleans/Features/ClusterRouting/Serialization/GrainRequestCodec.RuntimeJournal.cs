using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateRuntimeJournalRead(Guid requestId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalRead(database, clock,
            settings.RequestLifetime, requestId, RuntimeJournalIdentity.ProtectedPrincipalId, kind, payload));

    internal string CreateRuntimeJournalCommand(Guid requestId, string principalId, Guid commandId,
        ReadOnlyMemory<byte> payload)
        => Issue(GrainRequestCapabilityEnvelopes.RuntimeJournalCommand(database, clock,
            settings.RequestLifetime, requestId, principalId, commandId, payload));
}

using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateRuntimeJournalRead(Guid requestId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => Issue(new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = RuntimeJournalIdentity.ProtectedPrincipalId,
            ReadKind = kind,
            Payload = Encode(payload),
            ExpiresAt = clock.GetUtcNow() + settings.RequestLifetime
        });

    internal string CreateRuntimeJournalCommand(Guid requestId, string principalId, Guid commandId,
        ReadOnlyMemory<byte> payload)
        => Issue(new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            CommandKind = OperationKind.RuntimeJournal,
            CommandId = commandId,
            Payload = Encode(payload),
            ExpiresAt = clock.GetUtcNow() + settings.RequestLifetime
        });
}

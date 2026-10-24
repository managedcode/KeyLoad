using KeyLoad.Clustering.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateOnlineTextPublication(Guid actor, Command<DatabaseCommand> operation, DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Publication(this, clock.GetUtcNow(), settings.RequestLifetime,
            actor, operation, expiry));

    internal GrainRequestEnvelope CreateOnlineTextCapability(Guid actor, string principal, OnlineTextCapabilityRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Capability(this, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));

    internal GrainRequestEnvelope CreateOnlineTextProjectionRead(Guid actor, string principal, ReadProjectionBatchRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.ProjectionRead(this, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));

    internal GrainRequestEnvelope CreateOnlineTextCheckpoint(Guid actor, string principal, CommitProjectionBatchRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Checkpoint(this, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));
}

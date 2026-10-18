using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Clustering.Features.Search;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal string CreateOnlineTextPublication(Guid actor, Command<DatabaseCommand> operation, DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Publication(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, operation, expiry));

    internal string CreateOnlineTextCapability(Guid actor, string principal, OnlineTextCapabilityRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Capability(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));

    internal string CreateOnlineTextProjectionRead(Guid actor, string principal, ReadProjectionBatchRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.ProjectionRead(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));

    internal string CreateOnlineTextCheckpoint(Guid actor, string principal, CommitProjectionBatchRequest request,
        DateTimeOffset expiry)
        => Issue(OnlineTextRequestEnvelopes.Checkpoint(database, clock.GetUtcNow(), settings.RequestLifetime,
            actor, principal, request, expiry));
}

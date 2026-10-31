using KeyLoad.Clustering.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateOnlineTextPublication(Guid actor, Command<DatabaseCommand> operation)
        => Issue(OnlineTextRequestEnvelopes.Publication(this, actor, operation));

    internal GrainRequestEnvelope CreateOnlineTextCapability(Guid actor, string principal, OnlineTextCapabilityRequest request)
        => Issue(OnlineTextRequestEnvelopes.Capability(this, actor, principal, request));

    internal GrainRequestEnvelope CreateOnlineTextProjectionRead(Guid actor, string principal, ReadProjectionBatchRequest request)
        => Issue(OnlineTextRequestEnvelopes.ProjectionRead(this, actor, principal, request));

    internal GrainRequestEnvelope CreateOnlineTextCheckpoint(Guid actor, string principal, CommitProjectionBatchRequest request)
        => Issue(OnlineTextRequestEnvelopes.Checkpoint(this, actor, principal, request));
}

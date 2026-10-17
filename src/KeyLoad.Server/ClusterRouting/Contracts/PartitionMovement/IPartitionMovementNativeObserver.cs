namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Optional owner-local test observation; never part of a peer wire or authorization decision.</summary>
internal interface IPartitionMovementNativeObserver
{
    IPartitionMovementNativeObservation? Begin(Guid originalNonce, string receivingVoter);
}

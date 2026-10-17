namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Optional private observation of an actual already-admitted carrier; never supplies a result.</summary>
internal interface IControlledLifetimeReplayObserver
{
    bool Select(PartitionMovementTransportRequest original, CancellationToken token);
    ValueTask ObserveAsync(PartitionMovementTransportRequest original, ControlledLifetimeReplayExchange owner,
        CancellationToken token);
}

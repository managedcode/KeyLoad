namespace KeyLoad.Server.Features.ClusterRouting;

internal interface IPartitionMovementNativeObservation
{
    void Enter(PartitionMovementNativeObservationStage stage);
    void Complete(ErrorCode? error, bool authenticated, bool dispatched, Exception? original,
        Exception? secondary, PartitionMovementNativeObservationStage? failureStage);
}

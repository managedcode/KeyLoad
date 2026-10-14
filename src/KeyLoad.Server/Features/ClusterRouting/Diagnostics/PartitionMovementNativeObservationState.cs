namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementNativeObservationState(IPartitionMovementNativeObservation observer)
{
    internal bool Authenticated
    {
        get; set;
    }
    internal bool Dispatched
    {
        get; set;
    }
    internal bool OperationCompleted
    {
        get; set;
    }
    internal ErrorCode? Error
    {
        get; private set;
    }
    internal Exception? Failure
    {
        get; private set;
    }
    internal Exception? SecondaryFailure
    {
        get; private set;
    }
    internal PartitionMovementNativeObservationStage? FailureStage
    {
        get; private set;
    }
    private PartitionMovementNativeObservationStage stage;
    internal void Enter(PartitionMovementNativeObservationStage value)
    {
        stage = value;
        observer.Enter(value);
    }
    internal void Fail(Exception original, ErrorCode? code)
    {
        if (Failure is null)
        {
            Error = code;
            Failure = original;
            FailureStage = stage;
        }
        else if (!ReferenceEquals(Failure, original))
        {
            SecondaryFailure ??= original;
        }
    }
}

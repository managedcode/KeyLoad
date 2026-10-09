namespace KeyLoad.Server;

/// <summary>Common joined image/work ownership for original Capture and fresh transfer-data reads.</summary>
internal interface IPartitionMovementRetainedSourceImage : IAsyncDisposable
{
    PartitionRef Partition { get; }
    Guid MoveId { get; }
    Guid HandleId { get; }
    PartitionMovementImageSession Session { get; }
    void ReleaseWork();
}

namespace KeyLoad.Server;

/// <summary>Abort and shutdown join the original producer, including its terminal failure.</summary>
internal interface IPartitionMovementPendingSourceRead
{
    PartitionRef Partition { get; }
    Guid MoveId { get; }
    Task CompletionTask { get; }
    Task StopAsync();
}

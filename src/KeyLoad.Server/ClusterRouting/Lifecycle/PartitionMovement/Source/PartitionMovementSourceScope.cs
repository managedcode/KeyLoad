
namespace KeyLoad.Server;

/// <summary>Scopes retained source closure to the complete atomic partition and move identity.</summary>
internal readonly record struct PartitionMovementSourceScope(PartitionRef Partition, Guid MoveId);

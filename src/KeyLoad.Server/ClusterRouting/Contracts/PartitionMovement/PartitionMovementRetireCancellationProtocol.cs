namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementRetireCancellationProtocol
{
    internal const string CommandPath = PartitionMovementProtocol.Path + "/retire-cancellation";
    internal const string QueryPath = CommandPath + "/outcome";
}

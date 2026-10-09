namespace KeyLoad.Server.Features.ClusterRouting;

internal static partial class PartitionMovementWire
{
    internal static Task<byte[]> ReadRetireCancellationAsync(HttpRequest request, bool query,
        int maximumBytes, CancellationToken cancellationToken)
        => ReadExactAsync(request, query ? PartitionMovementRetireCancellationProtocol.QueryPath
            : PartitionMovementRetireCancellationProtocol.CommandPath, maximumBytes, cancellationToken);
}

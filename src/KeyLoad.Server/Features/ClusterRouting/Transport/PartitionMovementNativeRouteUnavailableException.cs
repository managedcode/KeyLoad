namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Records actual absence of an authenticated active native voter route before submission.</summary>
internal sealed class PartitionMovementNativeRouteUnavailableException : Exception
{
    private const int NoOriginalFailures = 0;

    public PartitionMovementNativeRouteUnavailableException() : base(PartitionMovementProtocol.Unavailable) { }
    public PartitionMovementNativeRouteUnavailableException(string? message) : base(message) { }
    public PartitionMovementNativeRouteUnavailableException(string? message, Exception? innerException) : base(message, innerException) { }
    internal PartitionMovementNativeRouteUnavailableException(IReadOnlyCollection<Exception> originalFailures)
        : base(PartitionMovementProtocol.Unavailable, originalFailures.Count == NoOriginalFailures ? null : new AggregateException(originalFailures)) { }
}

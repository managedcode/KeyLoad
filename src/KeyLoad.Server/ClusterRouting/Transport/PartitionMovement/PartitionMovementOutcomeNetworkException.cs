namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Retains only the actual outcome READ native route/send failure; resource cleanup is never classified for failover.</summary>
internal sealed class PartitionMovementOutcomeNetworkException : Exception
{
    public PartitionMovementOutcomeNetworkException()
        : base(PartitionMovementProtocol.OutcomeReadTransportFailed) { }

    public PartitionMovementOutcomeNetworkException(string? message)
        : base(message) { }

    public PartitionMovementOutcomeNetworkException(string? message, Exception? innerException)
        : base(message, innerException) { }

    internal PartitionMovementOutcomeNetworkException(Exception original)
        : base(PartitionMovementProtocol.OutcomeReadTransportFailed, original) { }
}

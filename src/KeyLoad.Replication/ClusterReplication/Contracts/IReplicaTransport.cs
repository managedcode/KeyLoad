namespace KeyLoad.Replication;

/// <summary>Invokes a bounded authenticated operation on one configured native replica service.</summary>
public interface IReplicaTransport
{
    /// <summary>Returns the peer's native typed outcome after authenticated envelope verification.</summary>
    /// <param name="voterId">Configured destination voter identity.</param>
    /// <param name="method">Authenticated replica operation.</param>
    /// <param name="payload">Exact bounded native replica payload.</param>
    /// <param name="cancellationToken">Caller deadline and cancellation.</param>
    /// <returns>The verified peer's native outcome bytes.</returns>
    Task<ReadOnlyMemory<byte>> InvokeAsync(string voterId, ReplicaRpc method, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

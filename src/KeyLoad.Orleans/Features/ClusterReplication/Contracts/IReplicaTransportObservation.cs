using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Observes the lifetime of actual authenticated native replica exchanges.</summary>
public interface IReplicaTransportObservation
{
    /// <summary>Captures an owner observation before an independently incoming Append is processed.</summary>
    /// <param name="method">The verified native protocol method.</param>
    /// <returns>An owner-bound callback invoked only after successful native reply construction.</returns>
    Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>>? BeginIncoming(ReplicaRpc method);
}

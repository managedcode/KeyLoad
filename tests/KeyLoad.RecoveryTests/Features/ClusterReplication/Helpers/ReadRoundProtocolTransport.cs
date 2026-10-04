using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

/// <summary>Forwards unchanged requests to real stored endpoints; this is not native Orleans qualification.</summary>
internal sealed class ReadRoundProtocolTransport(IReadOnlyDictionary<string, ReadRoundStoredNode> nodes) : IReplicaTransport
{
    private readonly long[] observed = new long[Enum.GetValues<ReplicaRpc>().Length];

    internal long Count(ReplicaRpc method) => Interlocked.Read(ref observed[(int)method]);

    /// <inheritdoc />
    public Task<ReadOnlyMemory<byte>> InvokeAsync(string voterId, ReplicaRpc method, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var reply = nodes[voterId].Consensus.HandleAsync(method, payload, cancellationToken);
        if (Enum.IsDefined(method))
        {
            Interlocked.Increment(ref observed[(int)method]);
        }
        return reply;
    }
}

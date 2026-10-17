namespace KeyLoad.Replication;

/// <summary>Retains one real local forward owner through synchronous native reply signing and publication admission.</summary>
internal interface IReplicaAuthenticatedForwardExchange
{
    bool RequiresOwnedForwardWork
    {
        get;
    }
    Task<T> HandleAuthenticatedForwardExchangeAsync<T>(ReadOnlyMemory<byte> payload,
        Func<ReadOnlyMemory<byte>, Exception?, T> createReply, Func<T, long> measureReply,
        Action<T> completed, CancellationToken cancellationToken);
}

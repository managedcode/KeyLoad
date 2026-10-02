using System.Text;

namespace KeyLoad.Replication;

internal sealed class ReplicaRpcClient(ReplicaConfiguration configuration, CancellationToken stoppingToken)
{
    private IReplicaTransport? transport;
    internal void Attach(IReplicaTransport value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref transport, value, null) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.InvalidPeer);
        }
    }

    internal async Task<TReply> InvokeAsync<TRequest, TReply>(string voter, ReplicaRpc method, TRequest request,
        CancellationToken cancellationToken)
    {
        var active = Volatile.Read(ref transport) ?? throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        var payload = ReplicaProtocolCodec.Serialize(request);
        if (payload.Length > configuration.MaxAppendBytes + ReplicaProtocol.PayloadMetadataBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidAppend);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stoppingToken);
        deadline.CancelAfter(configuration.RpcTimeout);
        try
        {
            var reply = await active.InvokeAsync(voter, method, Encoding.UTF8.GetString(payload), deadline.Token).ConfigureAwait(false);
            return ReplicaProtocolCodec.Deserialize<TReply>(Encoding.UTF8.GetBytes(reply));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader); }
    }

    internal static bool Unavailable(Exception error) => error is HttpRequestException or TimeoutException or OperationCanceledException
        || error is KeyLoadException
        {
            Code: ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome or ErrorCode.ResourceExhausted
            or ErrorCode.Unauthenticated or ErrorCode.Conflict or ErrorCode.NotFound
        };
}

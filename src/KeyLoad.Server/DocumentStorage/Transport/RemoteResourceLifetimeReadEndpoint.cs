using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.QueryExecution;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteResourceLifetimeReadEndpoint
{
    private readonly OrleansNode node;
    private readonly PartitionHost partition;
    private readonly ServerRuntimeOptions options;
    private readonly TimeProvider clock;
    private readonly RemoteDocumentLifetimeReceiver documents;
    private readonly RemoteQueryLifetimeReceiver queries;

    internal RemoteResourceLifetimeReadEndpoint(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, TimeProvider clock)
    {
        this.node = node;
        this.partition = partition;
        this.options = options;
        this.clock = clock;
        documents = new(node, partition, options, clock);
        queries = new(node, partition, options, clock);
    }

    internal async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(RemoteModelNativeRequest request,
        PartitionMovementReplayCache replay, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var call = NativeSerialization.Deserialize<RemoteResourceLifetimeReadCallV2>(request.Payload.Span);
        RemoteResourceLifetimeReadValidation.RequireCall(call, options, partition, clock, token);
        RemoteResourceLifetimeReadValidation.RequireEnvelope(request, call);
        if (!replay.TryUse(call.Nonce))
        {
            throw RemoteResourceLifetimeReadValidation.Invalid();
        }
        var remaining = call.ExpiresAt - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteResourceLifetimeReadProtocol.Unavailable);
        }
        using var expiry = new CancellationTokenSource(remaining, clock);
        using var original = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
        var reply = await ReadReplyAsync(call, binding, original.Token).ConfigureAwait(false);
        original.Token.ThrowIfCancellationRequested();
        RemoteResourceLifetimeReadValidation.RequireCall(call, options, partition, clock, original.Token);
        var bytes = RemoteDocumentWire.Encode(reply, call.MaximumReplyBytes);
        using var mac = RemoteDocumentMac.FromConfiguredSecret(options.Node.Value.PeerSecret);
        return (bytes, mac.Sign(bytes, reply: true));
    }

    private async Task<RemoteResourceLifetimeReadReplyV2> ReadReplyAsync(RemoteResourceLifetimeReadCallV2 call, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
    {
        ReadOnlyMemory<byte> result;
        try
        {
            result = await ExecuteModelAsync(call, binding, token).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code != ErrorCode.Corruption)
        {
            return new(RemoteResourceLifetimeReadProtocol.Version, call.Kind, call.RequestId, call.Nonce,
                ReadOnlyMemory<byte>.Empty, error.Code, McpReplyProtocol.SafeDetail(error.Code, error.Message), Discovery());
        }
        if (result.IsEmpty || result.Length > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteResourceLifetimeReadProtocol.Unavailable);
        }
        return new(RemoteResourceLifetimeReadProtocol.Version, call.Kind, call.RequestId, call.Nonce, result, null, null, Discovery());
    }

    private Task<ReadOnlyMemory<byte>> ExecuteModelAsync(RemoteResourceLifetimeReadCallV2 call, IResourceLifetimeWorkBinding binding,
        CancellationToken token)
        => call.Kind switch
        {
            RemoteResourceLifetimeReadKindV2.Document => documents.ExecuteAsync(call, binding, token),
            RemoteResourceLifetimeReadKindV2.PartitionQuery or RemoteResourceLifetimeReadKindV2.DistributedSearch => queries.ExecuteAsync(call, binding, token),
            _ => throw RemoteResourceLifetimeReadValidation.Invalid()
        };

    private ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteResourceLifetimeReadProtocol.Unavailable);
}

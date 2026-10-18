using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentClient : IAsyncDisposable
{
    private readonly NodeOptions options;
    private readonly IOptions<OrleansMembershipOptions> membership;
    private readonly TimeProvider clock;
    private readonly Func<RemoteModelNativeClient> borrowNative;
    private RemoteModelNativeClient? native;

    internal RemoteDocumentClient(IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membership, TimeProvider clock,
        Func<RemoteModelNativeClient> borrowNative)
    {
        this.options = options.Value;
        this.membership = membership;
        this.clock = clock;
        this.borrowNative = borrowNative;
    }

    internal Task<RemoteModelAuthenticatedReply> SendNativeAsync(RegisteredPhysicalOwnerV1 destination,
        PhysicalShardRecord source, RemoteModelNativeChannel channel, ReadOnlyMemory<byte> payload,
        string signature, DateTimeOffset expiresAt, long maximumReplyBytes, CancellationToken token)
        => Native.SendAsync(destination, source, channel, payload, signature, expiresAt, maximumReplyBytes, token);

    internal async Task RequireCurrentReplyAsync(RegisteredPhysicalOwnerV1 destination,
        RemoteModelAuthenticatedReply original, string innerAddress, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!PhysicalGroupAddress.CanonicalAddress(innerAddress, membership)
                || !string.Equals(original.DispatchSiloAddress, innerAddress, StringComparison.Ordinal))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
            }
            await Native.RequireCurrentAsync(destination, original.DispatchSiloAddress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception error) when (original.MayHaveEffects && NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, RemoteModelNativeProtocol.Unavailable, error);
        }
    }

    internal Task<ReadOnlyMemory<byte>> ReadLifetimeAsync(RemoteResourceLifetimeReadCallV2 call, CancellationToken token)
        => RemoteResourceLifetimeReadExchange.ReadAsync(this, options, membership, clock, call, token);

    internal Task<KeyLoad.Server.Features.EventStreams.EventVectorAuthenticatedExchange> SendEventVectorAsync(
        RegisteredPhysicalOwnerV1 destination, KeyLoad.Server.Features.EventStreams.EventVectorRemoteCall originalCall,
        KeyLoad.Server.Features.EventStreams.EventVectorRemoteEncoding encoding,
        KeyLoad.Core.ReadExecutionBudget work, CancellationToken originalCancellationToken)
        => KeyLoad.Server.Features.EventStreams.EventVectorRemoteExchange.SendAsync(this, options,
            membership, clock, destination, originalCall, encoding, work, originalCancellationToken);

    internal Task<KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerResult> TransferAsync(
        KeyLoad.Core.Features.Messaging.RemoteQueueTransferPeerCall call, CancellationToken token)
        => KeyLoad.Server.Features.Messaging.RemoteTransferPeerExchange.SendAsync(this, options, membership, clock, call, token);

    private RemoteModelNativeClient Native
    {
        get
        {
            var existing = Volatile.Read(ref native);
            if (existing is not null)
            {
                return existing;
            }
            var borrowed = borrowNative();
            return Interlocked.CompareExchange(ref native, borrowed, null) ?? borrowed;
        }
    }

    public ValueTask DisposeAsync()
        => Volatile.Read(ref native)?.DisposeAsync() ?? ValueTask.CompletedTask;
}

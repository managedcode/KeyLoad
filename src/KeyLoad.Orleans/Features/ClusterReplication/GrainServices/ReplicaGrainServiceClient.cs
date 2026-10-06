using KeyLoad.Replication;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Services;

namespace KeyLoad.Orleans;

/// <summary>Direct-address Orleans RPC transport with bounded generation rediscovery.</summary>
/// <param name="services">The active Orleans service provider used by the grain-service client.</param>
/// <param name="configurationOptions">The local voter identity and bounded RPC timeout.</param>
/// <param name="discovery">The authenticated fixed-voter runtime-address resolver.</param>
/// <param name="authentication">The request/reply envelope signer and verifier.</param>
/// <param name="transportOptions">Centrally validated attempt and envelope bounds.</param>
/// <param name="clock">Borrowed runtime clock for the bounded RPC deadline.</param>
public sealed class ReplicaGrainServiceClient(IServiceProvider services, IOptions<ReplicaConfiguration> configurationOptions,
    ReplicaSiloDiscoveryClient discovery, ReplicaEnvelopeAuthenticator authentication,
    IOptions<ReplicaTransportOptions> transportOptions, TimeProvider? clock = null)
    : GrainServiceClient<IPartitionReplicaGrainService>(services), IReplicaTransport
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly ReplicaConfiguration configuration = configurationOptions.Value;
    private readonly ReplicaTransportOptions settings = transportOptions.Value;
    /// <summary>Transports exact native bytes and retries only fenced/unavailable generation failures.</summary>
    /// <param name="voterId">The configured destination voter.</param>
    /// <param name="method">The replica operation to send.</param>
    /// <param name="payloadBytes">The exact native protocol payload.</param>
    /// <param name="cancellationToken">Caller cancellation for discovery and Orleans RPC.</param>
    /// <returns>The exact native reply bytes after authentication and error handling.</returns>
    public async Task<ReadOnlyMemory<byte>> InvokeAsync(string voterId, ReplicaRpc method, ReadOnlyMemory<byte> payloadBytes, CancellationToken cancellationToken)
    {
        const int AttemptInitialValue = 0;
        const int EmptyAttempt = 0;
        const int AttemptStep = 1;

        var payload = EncodePayload(payloadBytes);
        using var timeout = new CancellationTokenSource(configuration.RpcTimeout, time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var maximumAttempts = settings.MaximumAttempts;
            for (var attempt = AttemptInitialValue; attempt < maximumAttempts; attempt++)
            {
                try
                {
                    return await ExchangeAsync(voterId, method, payload, attempt != EmptyAttempt, deadline.Token).ConfigureAwait(false);
                }
                catch (OrleansMessageRejectionException) when (attempt + AttemptStep < maximumAttempts)
                {
                    // Rediscover the generation once; operation bytes and stable command IDs remain identical.
                }
            }

            throw Errors.Fail(InterruptedCode(method), ReplicaTransportProtocol.TransportUnavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Errors.Fail(InterruptedCode(method), ReplicaTransportProtocol.TransportUnavailable);
        }
        catch (Exception error) when (error is OrleansException or HttpRequestException or TimeoutException)
        {
            throw Errors.Fail(InterruptedCode(method), ReplicaTransportProtocol.TransportUnavailable);
        }

    }

    private async Task<ReadOnlyMemory<byte>> ExchangeAsync(string voterId, ReplicaRpc method, byte[] payload, bool refresh,
        CancellationToken cancellationToken)
    {
        var address = await discovery.ResolveAsync(voterId, refresh, cancellationToken).ConfigureAwait(false);
        var request = authentication.SignRequest(voterId, method, address.ToParsableString(), payload);
        var reply = await GetGrainService(address).ExchangeAsync(request, cancellationToken)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        authentication.VerifyReply(request, reply);
        reply = reply with { Payload = reply.Payload.ToArray(), Signature = reply.Signature.ToArray() };
        authentication.VerifyReply(request, reply);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? ReplicaTransportProtocol.EndpointFailure);
        }

        return reply.Payload;
    }

    private byte[] EncodePayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > authentication.MaximumPayloadBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded); }
        return payload.ToArray();
    }

    private static ErrorCode InterruptedCode(ReplicaRpc method) => method == ReplicaRpc.Forward
        ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
}

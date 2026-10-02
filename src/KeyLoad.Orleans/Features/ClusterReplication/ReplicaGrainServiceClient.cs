using System.Text;
using System.Text.Json;
using KeyLoad.Replication;
using Orleans.Runtime.Services;

namespace KeyLoad.Orleans;

/// <summary>Direct-address Orleans RPC transport with bounded generation rediscovery.</summary>
/// <param name="services">The active Orleans service provider used by the grain-service client.</param>
/// <param name="configuration">The local voter identity and bounded RPC timeout.</param>
/// <param name="discovery">The authenticated fixed-voter runtime-address resolver.</param>
/// <param name="authentication">The request/reply envelope signer and verifier.</param>
public sealed class ReplicaGrainServiceClient(IServiceProvider services, ReplicaConfiguration configuration,
    ReplicaSiloDiscoveryClient discovery, ReplicaEnvelopeAuthenticator authentication)
    : GrainServiceClient<IPartitionReplicaGrainService>(services), IReplicaTransport
{
    /// <summary>Transports exact UTF8 bytes and retries only fenced/unavailable generation failures.</summary>
    /// <param name="voterId">The configured destination voter.</param>
    /// <param name="method">The replica operation to send.</param>
    /// <param name="payloadJson">The exact protocol JSON string to encode as UTF8.</param>
    /// <param name="cancellationToken">Caller cancellation for discovery and Orleans RPC.</param>
    /// <returns>The exact UTF8 reply text after authentication and error handling.</returns>
    public async Task<string> InvokeAsync(string voterId, ReplicaRpc method, string payloadJson, CancellationToken cancellationToken)
    {
        var payload = EncodePayload(payloadJson);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(configuration.RpcTimeout);
        try
        {
            for (var attempt = 0; attempt < ReplicaTransportProtocol.MaximumAttempts; attempt++)
            {
                try
                {
                    return await ExchangeAsync(voterId, method, payload, attempt != 0, deadline.Token).ConfigureAwait(false);
                }
                catch (OrleansMessageRejectionException) when (attempt + 1 < ReplicaTransportProtocol.MaximumAttempts)
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
        catch (Exception error) when (error is DecoderFallbackException or JsonException or FormatException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }
    }

    private async Task<string> ExchangeAsync(string voterId, ReplicaRpc method, byte[] payload, bool refresh,
        CancellationToken cancellationToken)
    {
        var address = await discovery.ResolveAsync(voterId, refresh, cancellationToken).ConfigureAwait(false);
        var request = authentication.SignRequest(voterId, method, address.ToParsableString(), payload);
        var reply = await GetGrainService(address).ExchangeAsync(request, cancellationToken)
            .WaitAsync(cancellationToken).ConfigureAwait(false);
        authentication.VerifyReply(request, reply);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? ReplicaTransportProtocol.EndpointFailure);
        }

        return ReplicaTransportProtocol.Utf8.GetString(reply.Payload.Span);
    }

    private byte[] EncodePayload(string payloadJson)
    {
        try
        {
            if (ReplicaTransportProtocol.Utf8.GetByteCount(payloadJson) > authentication.MaximumPayloadBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
            }

            return ReplicaTransportProtocol.Utf8.GetBytes(payloadJson);
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    private static ErrorCode InterruptedCode(ReplicaRpc method) => method == ReplicaRpc.Forward
        ? ErrorCode.UnknownWriteOutcome : ErrorCode.OwnershipLost;
}

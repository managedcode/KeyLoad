using Microsoft.Extensions.Options;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Replication;

internal sealed class ReplicaRpcClient
{
    private readonly ReplicaConfiguration configuration;
    private readonly CancellationToken stoppingToken;

    internal ReplicaRpcClient(IOptions<ReplicaConfiguration> configurationOptions, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(configurationOptions);
        configuration = configurationOptions.Value;
        configuration.Validate();
        this.stoppingToken = stoppingToken;
    }
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
        var payload = EncodeRequest(request, configuration);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stoppingToken);
        deadline.CancelAfter(configuration.RpcTimeout);
        try
        {
            var transportStarted = DatabasePhaseTelemetry.Begin();
            var transportOutcome = DatabasePhaseOutcome.Faulted;
            ReadOnlyMemory<byte> reply;
            try
            {
                reply = await active.InvokeAsync(voter, method, payload, deadline.Token)
                    .ConfigureAwait(false);
                transportOutcome = DatabasePhaseOutcome.Completed;
            }
            catch (OperationCanceledException)
            {
                transportOutcome = DatabasePhaseTelemetry.CancellationOutcome(cancellationToken, deadline.Token);
                throw;
            }
            finally
            {
                DatabasePhaseTelemetry.End(DatabasePhaseKind.ReplicaTransportAwait, transportOutcome, transportStarted);
            }
            return DecodeReply<TReply>(reply, cancellationToken, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
    }

    private static byte[] EncodeRequest<TRequest>(TRequest request, ReplicaConfiguration configuration)
    {
        var encodeStarted = DatabasePhaseTelemetry.Begin();
        var encodeOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            var payload = ReplicaProtocolCodec.Serialize(request);
            if (payload.Length > configuration.MaxAppendBytes + ReplicaProtocol.PayloadMetadataBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidAppend);
            }
            encodeOutcome = DatabasePhaseOutcome.Completed;
            return payload;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ReplicaRequestEncode, encodeOutcome, encodeStarted);
        }
    }

    private static TReply DecodeReply<TReply>(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken,
        CancellationToken deadlineToken)
    {
        var decodeStarted = DatabasePhaseTelemetry.Begin();
        var decodeOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            var decoded = ReplicaProtocolCodec.Deserialize<TReply>(reply.Span);
            decodeOutcome = DatabasePhaseOutcome.Completed;
            return decoded;
        }
        catch (OperationCanceledException)
        {
            decodeOutcome = DatabasePhaseTelemetry.CancellationOutcome(cancellationToken, deadlineToken);
            throw;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ReplicaReplyDecode, decodeOutcome, decodeStarted);
        }
    }

    internal static bool Unavailable(Exception error) => error is HttpRequestException or TimeoutException or OperationCanceledException
        || error is KeyLoadException
        {
            Code: ErrorCode.OwnershipLost or ErrorCode.UnknownWriteOutcome or ErrorCode.ResourceExhausted
            or ErrorCode.Unauthenticated or ErrorCode.Conflict or ErrorCode.NotFound
        };
}

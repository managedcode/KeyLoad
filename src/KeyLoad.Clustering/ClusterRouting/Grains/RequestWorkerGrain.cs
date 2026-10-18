using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Performs fungible signed-request validation before a typed call to the actual storage owner.</summary>
[StatelessWorker]
internal sealed class RequestWorkerGrain(GrainRequestCodec codec, SiloDatabaseClient storage,
    IOptions<GrainRoutingOptions> options, ILogger<RequestWorkerGrain> diagnostics) : Grain, IRequestWorkerGrain
{
    public Task<GrainOperationReply> ExecuteAsync(SiloAddress owner, string signedRequest, CancellationToken cancellationToken)
        => ExecuteLeafAsync(owner, signedRequest, null, cancellationToken);

    public Task<GrainOperationReply> ExecuteCommandAsync(SiloAddress owner, string signedRequest,
        string actorKey, CancellationToken cancellationToken)
        => ExecuteLeafAsync(owner, signedRequest, actorKey, cancellationToken);

    private async Task<GrainOperationReply> ExecuteLeafAsync(SiloAddress owner, string signed,
        string? actorKey, CancellationToken token)
    {
        var command = false;
        var requestId = Guid.Empty;
        try
        {
            ArgumentNullException.ThrowIfNull(owner);
            token.ThrowIfCancellationRequested();
            var request = codec.Verify(signed);
            requestId = request.Envelope.RequestId;
            command = request.Envelope.CommandKind is not null;
            GrainIdentityContext.Validate(request.Envelope, requestId);
            if (actorKey is not null && (!command || GrainPartitionResolver.Resolve(request) != actorKey))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
            }
            var target = storage.On(owner);
            return actorKey is null
                ? await target.ExecuteAsync(signed, token).ConfigureAwait(true)
                : await target.ExecuteCommandAsync(signed, actorKey, token).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.Failure(error, command, diagnostics, options, requestId,
                GrainFailureStage.CapabilityExecution, token);
        }
    }
}

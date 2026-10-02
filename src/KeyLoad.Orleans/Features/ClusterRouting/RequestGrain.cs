using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Routes one independently keyed operation to a partition command actor or its own read actor.</summary>
/// <param name="codec">Shared database-signed request verifier.</param>
/// <param name="diagnostics">Operational logging for unexpected failures, without exception objects on the wire.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.RequestAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class RequestGrain(GrainRequestCodec codec, ILogger<RequestGrain> diagnostics) : Grain, IRequestGrain
{
    /// <inheritdoc />
    /// <param name="signedRequest">The database-signed request addressed to this unique request actor.</param>
    /// <param name="cancellationToken">Cancellation for validation and downstream capability execution.</param>
    /// <returns>A safe reply from the partition command actor or unique read actor.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
    {
        var command = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = codec.VerifyRequest(signedRequest, this.GetPrimaryKey());
            command = request.Envelope.CommandKind is not null;
            if (command)
            {
                var target = GrainFactory.GetGrain<ICommandPartitionGrain>(GrainPartitionResolver.Resolve(request));
                return await target.ExecuteAsync(signedRequest, cancellationToken).ConfigureAwait(true);
            }

            var reader = GrainFactory.GetGrain<IDatabaseReadGrain>(request.Envelope.RequestId);
            return await reader.ExecuteAsync(signedRequest, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error, command, diagnostics);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }
}

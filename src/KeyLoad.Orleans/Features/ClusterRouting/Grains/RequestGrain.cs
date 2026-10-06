using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Routes one independently keyed operation through the native bounded CQRS stream.</summary>
/// <param name="codec">Shared database-signed request verifier.</param>
/// <param name="diagnostics">Operational logging for unexpected failures, without exception objects on the wire.</param>
/// <param name="chunkSerializer">The actual registered Orleans serializer for request chunks.</param>
/// <param name="clock">Runtime clock for the finite execution deadline.</param>
/// <param name="workOwner">Silo-local owner for active request stream work.</param>
/// <param name="options">The centrally validated request execution snapshot.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.RequestAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class RequestGrain(GrainRequestCodec codec, ILogger<RequestGrain> diagnostics,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer, TimeProvider clock,
    NativeRequestWorkOwner workOwner, IOptions<GrainRoutingOptions> options)
    : Grain, IRequestGrain
{
    /// <inheritdoc />
    /// <param name="signedRequest">The database-signed request addressed to this unique request actor.</param>
    /// <param name="cancellationToken">Cancellation for validation and downstream capability execution.</param>
    /// <returns>A lazy native stream over the existing authorized capability.</returns>
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
    {
        var requestId = this.GetPrimaryKey();
        var phaseSettlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Action settled = phaseSettlement is null ? DeactivateOnIdle
            : () => phaseSettlement.Settle(((IGrainBase)this).GrainContext, DeactivateOnIdle);
        return NativeCqrsStreamLifetime.Run(
            createStream: token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteCapabilityAsync(signedRequest, requestId, writer, phaseSettlement), token),
            serializer: chunkSerializer, requestId: requestId, clock: clock, settled: settled, cancellationToken: cancellationToken, options: options, owner: workOwner);
    }

    private async ValueTask<Result<GrainOperationReply>> ExecuteCapabilityAsync(string signedRequest, Guid requestId,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        GrainRequestPhaseSettlement? phaseSettlement)
    {
        var command = false;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            writer.CancellationToken.ThrowIfCancellationRequested();
            var request = codec.VerifyRequest(signedRequest, requestId);
            GrainIdentityContext.Validate(request.Envelope, requestId);
            phaseSettlement?.SetIdentity(request);
            stage = GrainFailureStage.CapabilityExecution;
            command = request.Envelope.CommandKind is not null;
            await writer.StartedAsync(new GrainRequestProgress(requestId)).ConfigureAwait(true);
            if (codec.HasPhaseObserver)
            {
                await codec.ObservePhaseAsync(request, GrainRequestPhase.RequestStarted,
                    ((IGrainBase)this).GrainContext, writer.CancellationToken).ConfigureAwait(true);
            }

            writer.CancellationToken.ThrowIfCancellationRequested();
            GrainOperationReply reply;
            if (command)
            {
                stage = GrainFailureStage.PartitionResolution;
                var partition = GrainPartitionResolver.Resolve(request);
                stage = GrainFailureStage.CapabilityExecution;
                var target = GrainFactory.GetGrain<ICommandPartitionGrain>(partition);
                reply = await target.ExecuteAsync(signedRequest, writer.CancellationToken).ConfigureAwait(true);
            }
            else
            {
                stage = GrainFailureStage.CapabilityExecution;
                var reader = GrainFactory.GetGrain<IDatabaseReadGrain>(request.Envelope.RequestId);
                reply = await reader.ExecuteAsync(signedRequest, writer.CancellationToken).ConfigureAwait(true);
            }

            return GrainReplyFactory.StreamResult(reply);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.StreamResult(GrainReplyFactory.Failure(error, command, diagnostics,
                requestId, stage, writer.CancellationToken));
        }
    }
}

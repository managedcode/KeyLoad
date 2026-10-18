using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Executes authorized database work through native bounded CQRS streams at the storage owner.</summary>
/// <param name="codec">Shared database-signed request verifier.</param>
/// <param name="diagnostics">Operational logging for unexpected failures, without exception objects on the wire.</param>
/// <param name="chunkSerializer">The actual registered Orleans serializer for request chunks.</param>
/// <param name="clock">Runtime clock for the finite execution deadline.</param>
/// <param name="borrowWorkOwner">Borrows the silo-local owner for active request stream work.</param>
/// <param name="options">The centrally validated request execution snapshot.</param>
/// <param name="services">Borrowed native silo services for independent lane composition.</param>
[StatelessWorker]
internal sealed class RequestWorkerGrain(GrainRequestCodec codec, ILogger<RequestWorkerGrain> diagnostics,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer, TimeProvider clock,
    Func<NativeRequestWorkOwner> borrowWorkOwner, IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, IRequestWorkerGrain
{
    /// <inheritdoc />
    /// <param name="signedRequest">The database-signed operation executed by this connection owner.</param>
    /// <param name="cancellationToken">Cancellation for validation and downstream capability execution.</param>
    /// <returns>A lazy native stream over the existing authorized capability.</returns>
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
    {
        var requestId = OperationRequestId(signedRequest);
        var purpose = new NativeCqrsStreamPurpose();
        var workOwner = borrowWorkOwner();
        var phaseSettlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Func<ValueTask> settled = phaseSettlement is null ? static () => ValueTask.CompletedTask
            : () => phaseSettlement.SettleAsync(((IGrainBase)this).GrainContext);
        return NativeCqrsStreamLifetime.RunBoundWithAwaitedSettlement(
            createStream: token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteCapabilityAsync(signedRequest, requestId, writer, phaseSettlement, purpose, workOwner), token),
            serializer: chunkSerializer, requestId: requestId, clock: clock, settled: settled, cancellationToken: cancellationToken, options: options, owner: workOwner, purpose: purpose,
            databaseLimits: services.GetRequiredService<KeyLoad.Core.DatabaseEngine>().OperationLimitsOptions,
            acquireVerified: () => AcquireVerifiedProducer(signedRequest, requestId, workOwner));
    }

    public async Task<GrainOperationReply> ExecuteCommandAsync(string signedRequest, string actorKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = codec.Verify(signedRequest);
            var database = services.GetRequiredService<KeyLoad.Core.DatabaseEngine>();
            var executor = new GrainCommandExecutor(database,
                services.GetRequiredService<Func<KeyLoad.Core.ICommitCoordinator>>()(), clock, options, codec,
                borrowWorkOwner(), services.GetService<Func<INativeOnlineTextMaintenance>>()?.Invoke(),
                () => services.GetRequiredService<INativeDropTableExecution>());
            return await executor.ExecuteAsync(request, actorKey, cancellationToken,
                ((IGrainBase)this).GrainContext).ConfigureAwait(true);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error: error, command: true, diagnostics: diagnostics,
                cancellationToken: cancellationToken, options: options);
        }
    }

    private NativeRequestWorkLease AcquireVerifiedProducer(string signedRequest, Guid requestId,
        NativeRequestWorkOwner workOwner)
    {
        var request = codec.VerifyRequest(signedRequest, requestId);
        GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
        if (!NativeRequestWorkChildIdentity.IsSelectedPurpose(request.Envelope.Purpose))
        {
            return workOwner.Acquire(request.Envelope.RequestId, NativeRequestWorkKind.RequestProducer);
        }
        if (!NativeRequestWorkChildIdentity.IsSelected(request))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return workOwner.AcquireVerifiedProducer(request, services.GetRequiredService<PhysicalShardRecord>(),
            services.GetRequiredService<SiloStorageIdentityProvider>().Read());
    }

    private Guid OperationRequestId(string signedRequest)
    {
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state
            && state.RequestId != Guid.Empty)
        {
            return state.RequestId;
        }
        try
        {
            return codec.Verify(signedRequest).Envelope.RequestId;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return Guid.NewGuid();
        }
    }

    private async ValueTask<Result<GrainOperationReply>> ExecuteCapabilityAsync(string signedRequest, Guid requestId,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer,
        GrainRequestPhaseSettlement? phaseSettlement, NativeCqrsStreamPurpose purpose, NativeRequestWorkOwner workOwner)
    {
        var command = false;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            purpose.RequireAdmission();
            writer.CancellationToken.ThrowIfCancellationRequested();
            var request = codec.VerifyRequest(signedRequest, requestId);
            GrainIdentityContext.Validate(request.Envelope, request.Envelope.RequestId);
            phaseSettlement?.SetIdentity(request);
            purpose.Bind(request);
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
            if (request.Envelope.CommandKind is OperationKind.ReceiveAcrossLanes or OperationKind.MaintainAnnIndex or OperationKind.MaintainTextIndex or OperationKind.MaintainOnlineTextIndex)
            {
                reply = await NativeConnectionParentExecution.ExecuteAsync(request, ExecuteStreamAsync, services, codec,
                    clock, chunkSerializer, options, diagnostics, ((IGrainBase)this).GrainContext, writer, purpose).ConfigureAwait(true);
            }
            else if (command)
            {
                stage = GrainFailureStage.CapabilityExecution;
                reply = await NativeConnectionCommandExecution.ExecuteAsync(request, signedRequest, GrainFactory,
                    services, codec, clock, purpose.WorkBinding, writer.CancellationToken).ConfigureAwait(true);
            }
            else
            {
                stage = GrainFailureStage.CapabilityExecution;
                var reader = new ConnectionReadExecution(codec, services.GetRequiredService<KeyLoad.Core.DatabaseEngine>(),
                    services.GetRequiredService<Func<KeyLoad.Core.ICommitCoordinator>>()(), services, clock, workOwner,
                    diagnostics, ((IGrainBase)this).GrainContext);
                reply = await reader.ExecuteAsync(request, purpose.WorkBinding, purpose.RecordFailure,
                    writer.CancellationToken).ConfigureAwait(true);
            }

            return GrainReplyFactory.StreamResult(reply, options);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            purpose.RecordFailure(error);
            return GrainReplyFactory.StreamResult(reply: GrainReplyFactory.Failure(error: error, command: command, diagnostics: diagnostics,
                requestId: requestId, stage: stage, cancellationToken: writer.CancellationToken, options: options), options: options);
        }
    }

}

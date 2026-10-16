using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Owns parallel operations for one connection through native bounded CQRS streams.</summary>
/// <param name="codec">Shared database-signed request verifier.</param>
/// <param name="diagnostics">Operational logging for unexpected failures, without exception objects on the wire.</param>
/// <param name="chunkSerializer">The actual registered Orleans serializer for request chunks.</param>
/// <param name="clock">Runtime clock for the finite execution deadline.</param>
/// <param name="borrowWorkOwner">Borrows the silo-local owner for active request stream work.</param>
/// <param name="options">The centrally validated request execution snapshot.</param>
/// <param name="services">Borrowed native silo services for independent lane composition.</param>
[Orleans.Placement.Immovable(Orleans.Placement.ImmovableKind.Any)]
[Orleans.GrainType(GrainRoutingProtocol.RequestAlias), Orleans.Placement.PreferLocalPlacement]
public sealed class ConnectionGrain(GrainRequestCodec codec, ILogger<ConnectionGrain> diagnostics,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer, TimeProvider clock,
    Func<NativeRequestWorkOwner> borrowWorkOwner, IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, IConnectionGrain, IAsyncDisposable
{
    private readonly NativeConnectionOperationOwner operations = new(options);

    internal CancellationToken AdmissionClosedToken => operations.ShutdownToken;

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
        return NativeConnectionOperationStream.Run(operations, requestId, executionToken => NativeCqrsStreamLifetime.RunBoundWithAwaitedSettlement(
            createStream: token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteCapabilityAsync(signedRequest, requestId, writer, phaseSettlement, purpose, workOwner), token),
            serializer: chunkSerializer, requestId: requestId, clock: clock, settled: settled, cancellationToken: executionToken, options: options, owner: workOwner, purpose: purpose,
            databaseLimits: services.GetRequiredService<KeyLoad.Core.DatabaseEngine>().OperationLimitsOptions,
            acquireVerified: () => AcquireVerifiedProducer(signedRequest, requestId, workOwner)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(string signedControl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.VerifyConnectionClose(signedControl, this.GetPrimaryKey());
        try
        {
            await operations.CloseAsync().ConfigureAwait(true);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        var ownedFailure = await NativeRequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true);
        var nativeFailure = await NativeRequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true);
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(ownedFailure, nativeFailure));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();

    private NativeRequestWorkLease AcquireVerifiedProducer(string signedRequest, Guid requestId,
        NativeRequestWorkOwner workOwner)
    {
        var request = codec.VerifyRequest(signedRequest, requestId);
        GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
        if (!NativeRequestWorkChildIdentity.IsSelectedPurpose(request.Envelope.Purpose))
        {
            return workOwner.Acquire(request.Envelope.RequestId, NativeRequestWorkKind.RequestProducer);
        }
        if (!NativeRequestWorkChildIdentity.IsSelected(request))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return workOwner.AcquireVerifiedProducer(request, services.GetRequiredService<PhysicalShardRecord>(),
            services.GetRequiredService<ReplicaSiloDiscoveryState>().Read());
    }

    private Guid OperationRequestId(string signedRequest)
    {
        if (Orleans.Runtime.RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state
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
            GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
            phaseSettlement?.SetIdentity(request);
            purpose.Bind(request);
            stage = GrainFailureStage.CapabilityExecution;
            command = request.Envelope.CommandKind is not null && !EventVectorRequestScope.IsReceivingOnly(request);
            await writer.StartedAsync(new GrainRequestProgress(requestId)).ConfigureAwait(true);
            if (codec.HasPhaseObserver)
            {
                await codec.ObservePhaseAsync(request, GrainRequestPhase.RequestStarted,
                    ((IGrainBase)this).GrainContext, writer.CancellationToken).ConfigureAwait(true);
            }

            writer.CancellationToken.ThrowIfCancellationRequested();
            GrainOperationReply reply;
            var controlledReply = await TryExecuteControlledAsync(request, writer, purpose.WorkBinding).ConfigureAwait(true);
            if (controlledReply is not null)
            {
                return GrainReplyFactory.StreamResult(controlledReply, options);
            }
            if (request.Envelope.Purpose == ResourceRetirementCommandScope.Purpose
                || request.Envelope.Purpose is RemoteGraphRequestScope.ParentPurpose or EventVectorRequestScope.ParentPurpose
                || request.Envelope.CommandKind is OperationKind.ReceiveAcrossLanes or OperationKind.MaintainAnnIndex or OperationKind.MaintainTextIndex or OperationKind.MaintainOnlineTextIndex or OperationKind.MovePartition or OperationKind.ApplyPlacementIntent)
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

            return NativeCommandMetadataReplies.StreamResult(request, reply, options);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            purpose.RecordFailure(error);
            if (error is OperationCanceledException cancelled)
            {
                operations.ObserveOriginalCancellation(cancelled, requestId, stage, diagnostics, writer.CancellationToken);
            }
            return GrainReplyFactory.StreamResult(reply: GrainReplyFactory.Failure(error: error, command: command, diagnostics: diagnostics,
                requestId: requestId, stage: stage, cancellationToken: writer.CancellationToken, options: options), options: options);
        }
    }

    private async ValueTask<GrainOperationReply?> TryExecuteControlledAsync(DecodedGrainRequest request,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (request.Envelope.CommandKind != OperationKind.Batch
            && (request.Envelope.CommandKind is not { } kind || !BlobStorageOperations.Handles(kind)))
        {
            return null;
        }
        var controlled = services.GetService<IControlledDocumentCommandRouter>();
        if (controlled is null)
        {
            return null;
        }
        Func<CancellationToken, ValueTask>? grantSettled = null;
        Func<CancellationToken, ValueTask>? outcomeObserved = null;
        if (codec.HasPhaseObserver)
        {
            var context = ((IGrainBase)this).GrainContext;
            grantSettled = token => codec.ObservePhaseAsync(request, GrainRequestPhase.ControlledDocumentGrantSettled, context, token);
            outcomeObserved = token => codec.ObservePhaseAsync(request, GrainRequestPhase.ControlledDocumentOutcomeReturned, context, token);
        }
        var original = await controlled.TryExecuteAsync(request.Envelope, request.Payload, binding,
            writer.CancellationToken, grantSettled, outcomeObserved).ConfigureAwait(true);
        return original is null ? null : GrainReplyFactory.Operation(original, options, writer.CancellationToken);
    }

}

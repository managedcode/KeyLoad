using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

/// <summary>Owns parallel operations for one connection through native bounded CQRS streams.</summary>
/// <param name="codec">Shared database-signed request verifier.</param>
/// <param name="diagnostics">Operational logging for unexpected failures, without exception objects on the wire.</param>
/// <param name="chunkSerializer">The actual registered Orleans serializer for request chunks.</param>
/// <param name="clock">Runtime clock for the finite execution deadline.</param>
/// <param name="workOwner">Silo-local owner for active request stream work.</param>
/// <param name="options">The centrally validated request execution snapshot.</param>
/// <param name="services">Borrowed native silo services for independent lane composition.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.RequestAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class ConnectionGrain(GrainRequestCodec codec, ILogger<ConnectionGrain> diagnostics,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer, TimeProvider clock,
    NativeRequestWorkOwner workOwner, IOptions<GrainRoutingOptions> options, IServiceProvider services)
    : Grain, IConnectionGrain, IAsyncDisposable
{
    private readonly NativeConnectionOperationOwner operations = new(options);

    /// <inheritdoc />
    /// <param name="signedRequest">The database-signed operation executed by this connection owner.</param>
    /// <param name="cancellationToken">Cancellation for validation and downstream capability execution.</param>
    /// <returns>A lazy native stream over the existing authorized capability.</returns>
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken)
    {
        var requestId = OperationRequestId(signedRequest);
        var phaseSettlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Action settled = phaseSettlement is null ? static () => { }
            : () => phaseSettlement.Settle(((IGrainBase)this).GrainContext);
        return NativeConnectionOperationStream.Run(operations, requestId, executionToken => NativeCqrsStreamLifetime.Run(
            createStream: token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteCapabilityAsync(signedRequest, requestId, writer, phaseSettlement), token),
            serializer: chunkSerializer, requestId: requestId, clock: clock, settled: settled, cancellationToken: executionToken, options: options, owner: workOwner), cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(string signedControl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.VerifyConnectionClose(signedControl, this.GetPrimaryKey());
        try
        { await operations.CloseAsync().ConfigureAwait(true); }
        finally
        { DeactivateOnIdle(); }
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(global::Orleans.DeactivationReason reason, CancellationToken cancellationToken)
    {
        var ownedFailure = await NativeRequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true);
        var nativeFailure = await NativeRequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true);
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(ownedFailure, nativeFailure));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();

    private Guid OperationRequestId(string signedRequest)
    {
        if (global::Orleans.Runtime.RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is GrainRequestContextState state
            && state.RequestId != Guid.Empty)
        { return state.RequestId; }
        try
        { return codec.Verify(signedRequest).Envelope.RequestId; }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { return Guid.NewGuid(); }
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
            GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
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
            var controlledReply = await TryExecuteControlledAsync(request, writer).ConfigureAwait(true);
            if (controlledReply is not null)
            { return GrainReplyFactory.StreamResult(controlledReply, options); }
            if (request.Envelope.CommandKind is OperationKind.ReceiveAcrossLanes or OperationKind.MaintainAnnIndex or OperationKind.MaintainTextIndex or OperationKind.MovePartition)
            { reply = await ExecuteParentAsync(request, writer).ConfigureAwait(true); }
            else if (command)
            {
                stage = GrainFailureStage.PartitionResolution;
                var partition = PhysicalCommandActorKey.Resolve(request, services.GetService<IPhysicalRequestPlacement>());
                stage = GrainFailureStage.CapabilityExecution;
                var target = GrainFactory.GetGrain<ICommandPartitionGrain>(partition);
                reply = await target.ExecuteAsync(signedRequest, writer.CancellationToken).ConfigureAwait(true);
            }
            else
            {
                stage = GrainFailureStage.CapabilityExecution;
                var reader = new ConnectionReadExecution(codec, services.GetRequiredService<KeyLoad.Core.DatabaseEngine>(),
                    services.GetRequiredService<KeyLoad.Core.ICommitCoordinator>(), services, clock, workOwner,
                    diagnostics, ((IGrainBase)this).GrainContext);
                reply = await reader.ExecuteAsync(request, writer.CancellationToken).ConfigureAwait(true);
            }

            return GrainReplyFactory.StreamResult(reply: reply, options: options);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.StreamResult(reply: GrainReplyFactory.Failure(error: error, command: command, diagnostics: diagnostics,
                requestId: requestId, stage: stage, cancellationToken: writer.CancellationToken, options: options), options: options);
        }
    }

    private async ValueTask<GrainOperationReply?> TryExecuteControlledAsync(DecodedGrainRequest request,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        if (request.Envelope.CommandKind != OperationKind.Batch)
        { return null; }
        var controlled = services.GetService<IControlledDocumentCommandRouter>();
        if (controlled is null)
        { return null; }
        Func<CancellationToken, ValueTask>? grantSettled = null;
        Func<CancellationToken, ValueTask>? outcomeObserved = null;
        if (codec.HasPhaseObserver)
        {
            var context = ((IGrainBase)this).GrainContext;
            grantSettled = token => codec.ObservePhaseAsync(request, GrainRequestPhase.ControlledDocumentGrantSettled, context, token);
            outcomeObserved = token => codec.ObservePhaseAsync(request, GrainRequestPhase.ControlledDocumentOutcomeReturned, context, token);
        }
        var original = await controlled.TryExecuteAsync(request.Envelope, request.Payload,
            writer.CancellationToken, grantSettled, outcomeObserved).ConfigureAwait(true);
        return original is null ? null : GrainReplyFactory.Operation(original, options, writer.CancellationToken);
    }

    private async Task<GrainOperationReply> ExecuteParentAsync(DecodedGrainRequest request,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
    {
        if (request.Envelope.CommandKind == OperationKind.MovePartition)
        {
            var result = await PartitionMoveParentExecution.ExecuteAsync(request, services, clock, codec,
                ((IGrainBase)this).GrainContext, writer).ConfigureAwait(true);
            return EncodeParent(result, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainTextIndex)
        {
            var result = await TextMaintenanceExecution.ExecuteAsync(request, GrainFactory, services, codec,
                clock, chunkSerializer, options, diagnostics, writer).ConfigureAwait(true);
            return EncodeParent(result, writer.CancellationToken);
        }
        if (request.Envelope.CommandKind == OperationKind.MaintainAnnIndex)
        {
            var result = await AnnMaintenanceExecution.ExecuteAsync(request, GrainFactory, services, codec,
                clock, chunkSerializer, options, diagnostics, writer).ConfigureAwait(true);
            return EncodeParent(result, writer.CancellationToken);
        }
        var lanes = await MultiLaneReceiveExecution.ExecuteAsync(request, GrainFactory, services, codec,
            clock, chunkSerializer, options, diagnostics, writer.CancellationToken).ConfigureAwait(true);
        return EncodeParent(lanes, writer.CancellationToken);
    }

    private GrainOperationReply EncodeParent<T>(T result, CancellationToken token)
    {
        try
        { return GrainReplyFactory.Value(result, options, token); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            GrainFailureDiagnostics.Mark(error, GrainFailureStage.ReplyEncoding);
            throw;
        }
    }
}

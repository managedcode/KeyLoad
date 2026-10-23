using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Placement;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Owns one server-issued connection and joins its independent execution response streams.</summary>
[Immovable(ImmovableKind.Any), PreferLocalPlacement]
public sealed class ConnectionGrain(GrainRequestCodec codec, SiloDatabaseClient storage,
    IOptions<GrainRoutingOptions> options, ILogger<ConnectionGrain> diagnostics,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> serializer, TimeProvider clock,
    Func<RequestWorkOwner> borrowWorkOwner, IServiceProvider services)
    : Grain, IConnectionGrain, IAsyncDisposable
{
    private readonly ConnectionOperationOwner operations = new(options);
    internal CancellationToken AdmissionClosedToken => operations.ShutdownToken;

    /// <inheritdoc />
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        GrainRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        var request = codec.Decode(envelope);
        GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
        return ConnectionOperationStream.Run(operations, request.Envelope.RequestId,
            token => CreateStream(request, envelope, token), cancellationToken);
    }

    private IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> CreateStream(
        DecodedGrainRequest request, GrainRequestEnvelope envelope, CancellationToken token)
    {
        var requestId = request.Envelope.RequestId;
        var purpose = new CqrsStreamPurpose();
        var owner = borrowWorkOwner();
        var settlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Func<ValueTask> settled = settlement is null ? static () => ValueTask.CompletedTask
            : () => settlement.SettleAsync(((IGrainBase)this).GrainContext);
        return CqrsStreamLifetime.RunBoundWithAwaitedSettlement(
            execution => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteAsync(request, envelope, writer, purpose, settlement), execution),
            serializer, requestId, clock, settled, options, owner, purpose,
            services.GetRequiredService<IOptions<DatabaseLimits>>(),
            () => owner.Acquire(requestId, RequestWorkKind.RequestProducer), token);
    }

    private async ValueTask<Result<GrainOperationReply>> ExecuteAsync(DecodedGrainRequest request, GrainRequestEnvelope envelope,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, CqrsStreamPurpose purpose,
        GrainRequestPhaseSettlement? settlement)
    {
        var requestId = request.Envelope.RequestId;
        var command = false;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            purpose.RequireAdmission();
            writer.CancellationToken.ThrowIfCancellationRequested();
            codec.ValidateVerifiedScope(request.Envelope);
            GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
            settlement?.SetIdentity(request);
            purpose.Bind(request);
            command = request.Envelope.CommandKind is not null;
            await writer.StartedAsync(new GrainRequestProgress(requestId)).ConfigureAwait(true);
            if (codec.HasPhaseObserver)
            {
                await codec.ObservePhaseAsync(request, GrainRequestPhase.RequestStarted,
                    ((IGrainBase)this).GrainContext, writer.CancellationToken).ConfigureAwait(true);
            }
            stage = GrainFailureStage.CapabilityExecution;
            var owner = await storage.ReadOwnerAsync(request.Envelope.Incarnation, writer.CancellationToken).ConfigureAwait(true);
            var reply = await ExecuteLeafAsync(request, envelope, owner, purpose.WorkBinding,
                writer.CancellationToken).ConfigureAwait(true);
            if (ConnectionParentExecution.Handles(request.Envelope.CommandKind) && reply.Error is null)
            {
                reply = await ConnectionParentExecution.ExecuteAsync(request, envelope,
                    (child, cancellation) => ExecuteLeafAsync(codec.Decode(child), child, owner,
                        purpose.WorkBinding, cancellation),
                    (kind, id) => storage.AbortMaintenanceAsync(owner, kind, id), services, codec, clock,
                    options, diagnostics, ((IGrainBase)this).GrainContext, writer, purpose).ConfigureAwait(true);
            }
            return GrainReplyFactory.StreamResult(reply, options);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            purpose.RecordFailure(error);
            return GrainReplyFactory.StreamResult(GrainReplyFactory.Failure(error, command, diagnostics,
                options, requestId, stage, writer.CancellationToken), options);
        }
    }

    private Task<GrainOperationReply> ExecuteLeafAsync(DecodedGrainRequest request, GrainRequestEnvelope envelope,
        SiloAddress owner, RequestWorkLease binding, CancellationToken token)
        => request.Envelope.CommandKind is not null && !ConnectionParentExecution.Handles(request.Envelope.CommandKind)
            ? ConnectionCommandExecution.ExecuteAsync(request, envelope, GrainFactory,
                services, codec, clock, binding, token)
            : storage.ExecuteAsync(owner, envelope, token);

    /// <inheritdoc />
    public async Task CloseAsync(GrainConnectionCloseControl closeControl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.ValidateConnectionClose(closeControl, this.GetPrimaryKey());
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
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        var owned = await RequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true);
        var execution = await RequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true);
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(owned, execution));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();
}

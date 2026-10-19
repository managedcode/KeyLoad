using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Serialization;

namespace KeyLoad.Clustering;

/// <summary>Owns one server-issued connection and joins its independent execution response streams.</summary>
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
        string signedRequest, CancellationToken cancellationToken)
    {
        var request = codec.Verify(signedRequest);
        GrainIdentityContext.ValidateConnection(request.Envelope, this.GetPrimaryKey());
        return ConnectionOperationStream.Run(operations, request.Envelope.RequestId,
            token => CreateStream(signedRequest, request.Envelope.RequestId, token), cancellationToken);
    }

    private IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> CreateStream(
        string signed, Guid requestId, CancellationToken token)
    {
        var purpose = new CqrsStreamPurpose();
        var owner = borrowWorkOwner();
        var settlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Func<ValueTask> settled = settlement is null ? static () => ValueTask.CompletedTask
            : () => settlement.SettleAsync(((IGrainBase)this).GrainContext);
        return CqrsStreamLifetime.RunBoundWithAwaitedSettlement(
            execution => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => ExecuteAsync(signed, requestId, writer, purpose, settlement), execution),
            serializer, requestId, clock, settled, options, owner, purpose,
            services.GetRequiredService<IOptions<DatabaseLimits>>(),
            () => owner.Acquire(requestId, RequestWorkKind.RequestProducer), token);
    }

    private async ValueTask<Result<GrainOperationReply>> ExecuteAsync(string signed, Guid requestId,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, CqrsStreamPurpose purpose,
        GrainRequestPhaseSettlement? settlement)
    {
        var command = false;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            purpose.RequireAdmission();
            writer.CancellationToken.ThrowIfCancellationRequested();
            var request = codec.VerifyRequest(signed, requestId);
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
            var reply = await ExecuteLeafAsync(request, signed, owner, purpose.WorkBinding,
                writer.CancellationToken).ConfigureAwait(true);
            if (ConnectionParentExecution.Handles(request.Envelope.CommandKind) && reply.Error is null)
            {
                reply = await ConnectionParentExecution.ExecuteAsync(request, signed,
                    (child, cancellation) => ExecuteLeafAsync(codec.Verify(child), child, owner,
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

    private Task<GrainOperationReply> ExecuteLeafAsync(DecodedGrainRequest request, string signed,
        SiloAddress owner, RequestWorkLease binding, CancellationToken token)
        => request.Envelope.CommandKind is not null && !ConnectionParentExecution.Handles(request.Envelope.CommandKind)
            ? ConnectionCommandExecution.ExecuteAsync(request, signed, GrainFactory,
                services, codec, clock, binding, token)
            : storage.ExecuteAsync(owner, signed, token);

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
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        var owned = await RequestWorkSettlement.JoinAsync(DisposeAsync().AsTask()).ConfigureAwait(true);
        var execution = await RequestWorkSettlement.JoinAsync(base.OnDeactivateAsync(reason, cancellationToken)).ConfigureAwait(true);
        RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(owned, execution));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => operations.DisposeAsync();
}

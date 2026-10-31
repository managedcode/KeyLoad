using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Orleans.Placement;

namespace KeyLoad.Clustering;

/// <summary>Coordinates one operation: resolves its storage owner, runs it there or beside it, and joins every child.</summary>
[Immovable(ImmovableKind.Any), PreferLocalPlacement]
public sealed class RequestGrain(GrainRequestCodec codec, SiloDatabaseClient storage, ILocalDatabaseRuntime runtime,
    ILocalSiloDetails local, ILogger<RequestGrain> diagnostics, TimeProvider clock,
    Func<RequestWorkOwner> borrowWorkOwner, IServiceProvider services)
    : Grain, IRequestGrain
{
    /// <inheritdoc />
    public async Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var request = DecodeRequest(envelope);
            return await GrainRequestStreamConsumer.DrainWithPurposeAsync(
                token => CreateStream(request, envelope, token), request.Envelope.RequestId, new CqrsStreamPurpose(() => request), cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        GrainRequestEnvelope envelope, CancellationToken cancellationToken)
        => CreateStream(DecodeRequest(envelope), envelope, cancellationToken);

    /// <inheritdoc />
    public Task ReleaseAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private DecodedGrainRequest DecodeRequest(GrainRequestEnvelope envelope)
    {
        var request = codec.Decode(envelope);
        GrainRequestScope.RequireRequestIdentity(request.Envelope, this.GetPrimaryKey());
        GrainIdentityContext.Validate(request.Envelope, this.GetPrimaryKey());
        return request;
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
            requestId, settled, owner, purpose,
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
            GrainIdentityContext.Validate(request.Envelope, this.GetPrimaryKey());
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
                    (kind, id) => storage.AbortMaintenanceAsync(owner, kind, id), services, codec,
                    diagnostics, ((IGrainBase)this).GrainContext, writer, purpose).ConfigureAwait(true);
            }
            return GrainReplyFactory.StreamResult(reply);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            purpose.RecordFailure(error);
            return GrainReplyFactory.StreamResult(GrainReplyFactory.Failure(error, command, diagnostics, requestId, stage, writer.CancellationToken));
        }
    }

    private Task<GrainOperationReply> ExecuteLeafAsync(DecodedGrainRequest request, GrainRequestEnvelope envelope,
        SiloAddress owner, RequestWorkLease binding, CancellationToken token)
        => request.Envelope.CommandKind is not null && !ConnectionParentExecution.Handles(request.Envelope.CommandKind)
            ? ConnectionCommandExecution.ExecuteAsync(request, envelope, RunOnOwnerAsync, owner, codec, clock, binding, token)
            : RunOnOwnerAsync(owner, envelope, null, token);

    /// <summary>Runs one leaf beside its storage owner: here when this silo owns it, otherwise on the owner's silo.</summary>
    private Task<GrainOperationReply> RunOnOwnerAsync(SiloAddress owner, GrainRequestEnvelope envelope, string? actorKey,
        CancellationToken token)
    {
        if (!owner.Equals(local.SiloAddress))
        {
            return storage.ExecuteAsync(owner, envelope, token, actorKey);
        }
        GrainIdentityContext.Validate(envelope, envelope.RequestId);
        return runtime.ExecuteAsync(envelope, actorKey, ((IGrainBase)this).GrainContext, token);
    }
}

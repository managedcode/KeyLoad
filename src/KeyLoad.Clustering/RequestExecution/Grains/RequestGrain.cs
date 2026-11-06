using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Logging;
using Orleans.Placement;

namespace KeyLoad.Clustering;

/// <summary>Coordinates one operation: resolves its storage owner, runs it there or beside it, and joins every child.</summary>
/// <remarks>Each request has its own ordinary Orleans activation, placed beside the connection when possible.
/// Cancellation and stream messages address that request's activation.</remarks>
[PreferLocalPlacement]
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
            if (request.Envelope.ReadKind is GrainReadKind.Document or GrainReadKind.Query or GrainReadKind.AstQuery
                || request.Envelope.CommandKind == OperationKind.Batch)
            {
                return await ExecuteUnaryAsync(request, cancellationToken);
            }
            return await GrainRequestStreamConsumer.DrainWithPurposeAsync(
                token => CreateStream(request, envelope, token), request.Envelope.RequestId, new CqrsStreamPurpose(() => request), cancellationToken);
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
                writer => ExecuteAsync(request, envelope, writer, purpose, settlement, writer.CancellationToken), execution),
            requestId, settled, owner, purpose,
            () => owner.Acquire(requestId, RequestWorkKind.RequestProducer), token);
    }

    private async Task<GrainOperationReply> ExecuteUnaryAsync(DecodedGrainRequest request, CancellationToken token)
    {
        var owner = borrowWorkOwner();
        var purpose = new CqrsStreamPurpose();
        var settlement = codec.HasPhaseObserver ? new GrainRequestPhaseSettlement(codec) : null;
        Func<ValueTask> settled = settlement is null ? static () => ValueTask.CompletedTask
            : () => settlement.SettleAsync(((IGrainBase)this).GrainContext);
        try
        {
            return await BoundCqrsStreamLifetime.RunUnaryAsync(owner, purpose, request.Envelope.RequestId,
                execution => ExecuteAsync(request, request.Envelope, null, purpose, settlement, execution), settled,
                token);
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested
            && error.CancellationToken != token && CqrsBoundaryErrors.IsNonFatal(error))
        {
            throw new OperationCanceledException(error.Message, error, token);
        }
        catch (AggregateException error) when (CqrsRuntimeFailures.FindFatal(error) is { } fatal)
        {
            RequestWorkSettlement.Rethrow(fatal);
            throw;
        }
    }

    private async ValueTask<Result<GrainOperationReply>> ExecuteAsync(DecodedGrainRequest request, GrainRequestEnvelope envelope,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply>? writer, CqrsStreamPurpose purpose,
        GrainRequestPhaseSettlement? settlement, CancellationToken token)
    {
        var requestId = request.Envelope.RequestId;
        var command = false;
        var stage = GrainFailureStage.EnvelopeVerification;
        try
        {
            purpose.RequireAdmission();
            token.ThrowIfCancellationRequested();
            codec.ValidateVerifiedScope(request.Envelope);
            GrainIdentityContext.Validate(request.Envelope, this.GetPrimaryKey());
            settlement?.SetIdentity(request);
            purpose.Bind(request);
            command = request.Envelope.CommandKind is not null;
            if (writer is not null)
            {
                await writer.StartedAsync(new GrainRequestProgress(requestId));
            }
            if (codec.HasPhaseObserver)
            {
                await codec.ObservePhaseAsync(request, GrainRequestPhase.RequestStarted,
                    ((IGrainBase)this).GrainContext, token);
            }
            stage = GrainFailureStage.CapabilityExecution;
            var owner = await storage.RequestOwnerAsync(request.Envelope.Incarnation, token);
            var reply = await ExecuteLeafAsync(request, envelope, owner, purpose.WorkBinding,
                token);
            if (writer is not null && ConnectionParentExecution.Handles(request.Envelope.CommandKind) && reply.Error is null)
            {
                reply = await ConnectionParentExecution.ExecuteAsync(request, envelope,
                    (child, cancellation) => ExecuteLeafAsync(codec.Decode(child), child, owner,
                        purpose.WorkBinding, cancellation),
                    (kind, id) => storage.AbortMaintenanceAsync(owner, kind, id), services, codec,
                    diagnostics, ((IGrainBase)this).GrainContext, writer, purpose);
            }
            return GrainReplyFactory.StreamResult(reply);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            purpose.RecordFailure(error);
            return GrainReplyFactory.StreamResult(GrainReplyFactory.Failure(error, command, diagnostics, requestId, stage, token));
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

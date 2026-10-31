using KeyLoad.Clustering;
using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using KeyLoad.Core.Search;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server;

/// <summary>Owns accepted storage Tasks and joins their actual local leases before session cleanup.</summary>
internal sealed class PartitionStorageRequestExecution(DatabaseEngine database, RequestWorkOwner workOwner,
    GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    ILogger diagnostics)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (GrainReadKind Kind, RequestWorkLease Work)> active = [];

    internal async Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, string? actorKey, IGrainContext context,
        CancellationToken token)
    {
        var requestId = Guid.Empty;
        var command = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var request = codec.Decode(envelope);
            requestId = request.Envelope.RequestId;
            command = request.Envelope.CommandKind is not null;
            GrainIdentityContext.Validate(request.Envelope, requestId);
            return await RunOwnedAsync(request, actorKey, context, token).ConfigureAwait(true);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.Failure(error, command, diagnostics, requestId,
                GrainFailureStage.CapabilityExecution, token);
        }
    }

    private async Task<GrainOperationReply> RunOwnedAsync(DecodedGrainRequest request, string? actorKey,
        IGrainContext context, CancellationToken token)
    {
        var work = (CapabilityWorkLifetime.Acquire(workOwner, request.Envelope.RequestId,
            RequestWorkKind.RuntimeTransport, token)).Value
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var original = work.OriginalProducer;
        var sessionId = Guid.Empty;
        var registered = false;
        try
        {
            await using (work)
            {
                try
                {
                    sessionId = SessionId(request);
                    Register(sessionId, request.Envelope.ReadKind ?? GrainReadKind.OnlineTextMaintenance, original);
                    registered = sessionId != Guid.Empty;
                    return await ExecuteAdmittedAsync(request, actorKey, context, work).ConfigureAwait(true);
                }
                catch (Exception failure)
                {
                    work.PrimaryError = CapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                    throw;
                }
            }
        }
        finally
        {
            if (registered && original.Settlement.IsCompleted)
            {
                lock (gate)
                {
                    _ = active.Remove(sessionId);
                }
            }
        }
    }

    private Task<GrainOperationReply> ExecuteAdmittedAsync(DecodedGrainRequest request, string? actorKey,
        IGrainContext context, CapabilityWorkLifetime work)
    {
        if (actorKey is not null)
        {
            var executor = new GrainCommandExecutor(database, services.GetRequiredService<Func<ICommitCoordinator>>()(),
                clock, codec, workOwner,
                services.GetService<Func<IOnlineTextMaintenance>>()?.Invoke(),
                () => services.GetRequiredService<IDropTableExecution>());
            return executor.ExecuteAsync(request, actorKey, work.Token, context);
        }
        return ExecuteReadAsync(request, context, work);
    }

    private async Task<GrainOperationReply> ExecuteReadAsync(DecodedGrainRequest request, IGrainContext context,
        CapabilityWorkLifetime work)
    {
        if (ConnectionParentExecution.Handles(request.Envelope.CommandKind))
        {
            await services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync(work.Token).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, work.Token);
            ConnectionParentExecution.Authorize(database, request, clock);
            return GrainReplyFactory.Value(true);
        }
        if (request.Envelope.CommandKind is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        }
        var reader = new ConnectionReadExecution(codec, database,
            services.GetRequiredService<Func<ICommitCoordinator>>()(), services, clock, workOwner, diagnostics, context);
        return await reader.ExecuteAsync(request, work, error => work.PrimaryError = error, work.Token).ConfigureAwait(true);
    }

    private Guid SessionId(DecodedGrainRequest request)
    {
        var maintenance = request.Envelope.ReadKind is GrainReadKind.AnnMaintenance
            or GrainReadKind.TextMaintenance or GrainReadKind.OnlineTextMaintenance;
        var checkpoint = request.Envelope.CommandKind == OperationKind.CommitProjectionBatch
            && request.Envelope.Purpose == OnlineTextRequestScope.Purpose;
        if (!maintenance && !checkpoint && request.Envelope.CommandKind != OperationKind.OnlineTextPublicationPhase)
        {
            return Guid.Empty;
        }
        GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(database, request.Envelope, clock));
        var id = request.Envelope.ReadKind switch
        {
            GrainReadKind.AnnMaintenance => GrainPayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload).SessionId,
            GrainReadKind.TextMaintenance => GrainPayload.Read<TextMaintenanceCapabilityRequest>(request.Payload).SessionId,
            GrainReadKind.OnlineTextMaintenance => GrainPayload.Read<OnlineTextCapabilityRequest>(request.Payload).SessionId,
            _ => Guid.Empty
        };
        if (request.Envelope.CommandKind == OperationKind.OnlineTextPublicationPhase)
        {
            id = OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(
                GrainPayload.Read<Command<DatabaseCommand>>(request.Payload)).SessionId;
        }
        if (checkpoint)
        {
            var original = GrainPayload.ReadCommand<CommitProjectionBatchRequest>(request.Payload);
            id = services.GetRequiredService<Func<IOnlineTextMaintenance>>()()
                .TryRequireCheckpointWork(original, request.Envelope.PrincipalId!)?.SessionId
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        if (id == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return id;
    }

    private void Register(Guid id, GrainReadKind kind, RequestWorkLease original)
    {
        if (id == Guid.Empty)
        {
            return;
        }
        lock (gate)
        {
            EvictSettled();
            if (!active.TryAdd(id, (kind, original)))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
        }
    }

    /// <summary>A session whose original producer settled after its request returned holds no abortable work.</summary>
    private void EvictSettled()
    {
        var settled = active.Where(entry => entry.Value.Work.Settlement.IsCompleted)
            .Select(entry => entry.Key).ToArray();
        foreach (var id in settled)
        {
            _ = active.Remove(id);
        }
    }

    internal async Task AbortAsync(GrainReadKind kind, Guid id)
    {
        if (id == Guid.Empty || kind is not (GrainReadKind.AnnMaintenance
            or GrainReadKind.TextMaintenance or GrainReadKind.OnlineTextMaintenance))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        RequestWorkLease? original;
        lock (gate)
        {
            var found = active.TryGetValue(id, out var current);
            if (found && current.Kind != kind)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
            original = found ? current.Work : null;
        }
        var failures = new List<Exception>();
        if (original is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (await original.CancelAsync() is { } cancellation)
                {
                    failures.Add(cancellation);
                }
                var settled = await original.Settlement;
                if (settled.CleanupOrJoinFailure is { } cleanup)
                {
                    failures.Add(cleanup);
                }
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => kind switch
        {
            GrainReadKind.AnnMaintenance => services.GetRequiredService<Func<IAnnMaintenance>>()().AbortAsync(id),
            GrainReadKind.TextMaintenance => services.GetRequiredService<Func<ITextMaintenance>>()().AbortAsync(id),
            GrainReadKind.OnlineTextMaintenance => services.GetRequiredService<Func<IOnlineTextMaintenance>>()().AbortAsync(id),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

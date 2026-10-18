using KeyLoad.Clustering;
using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns accepted storage Tasks and joins their actual local leases before session cleanup.</summary>
internal sealed class PartitionStorageRequestExecution(DatabaseEngine database, NativeRequestWorkOwner workOwner,
    GrainRequestCodec codec, IServiceProvider services, TimeProvider clock,
    IOptions<GrainRoutingOptions> options, ILogger diagnostics, IGrainContext context)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (GrainReadKind Kind, NativeRequestWorkLease Work)> active = [];

    internal async Task<GrainOperationReply> ExecuteAsync(string signed, string? actorKey, CancellationToken token)
    {
        var requestId = Guid.Empty;
        var command = false;
        try
        {
            token.ThrowIfCancellationRequested();
            var request = codec.Verify(signed);
            requestId = request.Envelope.RequestId;
            command = request.Envelope.CommandKind is not null;
            GrainIdentityContext.Validate(request.Envelope, requestId);
            return await RunOwnedAsync(request, actorKey, token).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return GrainReplyFactory.Failure(error, command, diagnostics, options, requestId,
                GrainFailureStage.CapabilityExecution, token);
        }
    }

    private async Task<GrainOperationReply> RunOwnedAsync(DecodedGrainRequest request, string? actorKey, CancellationToken token)
    {
        var work = NativeCapabilityWorkLifetime.Acquire(workOwner, request.Envelope.RequestId,
            NativeRequestWorkKind.NativeTransport, database.OperationLimitsOptions, token)
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
                    return await ExecuteAdmittedAsync(request, actorKey, work).ConfigureAwait(true);
                }
                catch (Exception failure)
                {
                    work.PrimaryError = NativeCapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
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
        NativeCapabilityWorkLifetime work)
    {
        if (actorKey is not null)
        {
            var executor = new GrainCommandExecutor(database, services.GetRequiredService<Func<ICommitCoordinator>>()(),
                clock, options, codec, workOwner,
                services.GetService<Func<INativeOnlineTextMaintenance>>()?.Invoke(),
                () => services.GetRequiredService<INativeDropTableExecution>());
            return executor.ExecuteAsync(request, actorKey, work.Token, context);
        }
        return ExecuteReadAsync(request, work);
    }

    private async Task<GrainOperationReply> ExecuteReadAsync(DecodedGrainRequest request, NativeCapabilityWorkLifetime work)
    {
        if (NativeConnectionParentExecution.Handles(request.Envelope.CommandKind))
        {
            await services.GetRequiredService<Func<ICommitCoordinator>>()().ReadBarrierAsync(work.Token).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, work.Token);
            NativeConnectionParentExecution.Authorize(database, request, clock);
            return GrainReplyFactory.Value(true, options, work.Token);
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
            GrainReadKind.AnnMaintenance => GrainNativePayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload).SessionId,
            GrainReadKind.TextMaintenance => GrainNativePayload.Read<TextMaintenanceCapabilityRequest>(request.Payload).SessionId,
            GrainReadKind.OnlineTextMaintenance => GrainNativePayload.Read<OnlineTextCapabilityRequest>(request.Payload).SessionId,
            _ => Guid.Empty
        };
        if (request.Envelope.CommandKind == OperationKind.OnlineTextPublicationPhase)
        {
            id = DatabaseEngine.Payload<OnlineTextPublicationPhaseCommand>(
                GrainNativePayload.Read<Command<DatabaseCommand>>(request.Payload)).SessionId;
        }
        if (checkpoint)
        {
            var original = GrainNativePayload.ReadCommand<CommitProjectionBatchRequest>(request.Payload);
            id = services.GetRequiredService<Func<INativeOnlineTextMaintenance>>()()
                .TryRequireCheckpointWork(original, request.Envelope.PrincipalId!, request.Envelope.ExpiresAt)?.SessionId
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        if (id == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return id;
    }

    private void Register(Guid id, GrainReadKind kind, NativeRequestWorkLease original)
    {
        if (id == Guid.Empty)
        {
            return;
        }
        lock (gate)
        {
            if (!active.TryAdd(id, (kind, original)))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
        }
    }

    internal async Task AbortAsync(GrainReadKind kind, Guid id)
    {
        if (id == Guid.Empty || kind is not (GrainReadKind.AnnMaintenance
            or GrainReadKind.TextMaintenance or GrainReadKind.OnlineTextMaintenance))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        NativeRequestWorkLease? original;
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
                if (await original.CancelAsync().ConfigureAwait(false) is { } cancellation)
                {
                    failures.Add(cancellation);
                }
                var settled = await original.Settlement.ConfigureAwait(false);
                if (settled.CleanupOrJoinFailure is { } cleanup)
                {
                    failures.Add(cleanup);
                }
            }, failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => kind switch
        {
            GrainReadKind.AnnMaintenance => services.GetRequiredService<Func<INativeAnnMaintenance>>()().AbortAsync(id),
            GrainReadKind.TextMaintenance => services.GetRequiredService<Func<INativeTextMaintenance>>()().AbortAsync(id),
            GrainReadKind.OnlineTextMaintenance => services.GetRequiredService<Func<INativeOnlineTextMaintenance>>()().AbortAsync(id),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}

using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Orleans;

/// <summary>Owns one operation's authorized capability dispatch and temporary read helpers.</summary>
internal sealed class ConnectionReadCapabilities(DatabaseEngine localDatabase, IServiceProvider services,
    TimeProvider runtimeClock, GrainRequestCodec codec, IGrainContext context)
{
    private readonly GrainCoreReadCapabilities core = new(localDatabase);
    private GrainQueryReadCapabilities? query;
    private GrainQueryReadCapabilities Query => query ??= new(services.GetRequiredService<QueryEngine>(),
        services.GetRequiredService<SearchEngine>(), runtimeClock, services.GetRequiredService<PhysicalShardRecord>());
    private INodeAdministration Administration => services.GetRequiredService<INodeAdministration>();
    private readonly GrainBlobReadExecution blobs = new(localDatabase, services);

    internal async Task<object?> ReadAsync(DecodedGrainRequest request, Guid requestId,
        CancellationToken cancellationToken)
    {
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.Authenticate)
        {
            return GrainRequestAuthority.Authenticate(localDatabase, request.Payload, runtimeClock);
        }

        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.AuthorizationReload,
                context, cancellationToken).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, requestId, cancellationToken);
        }

        var principal = GrainRequestAuthority.ReloadForRequest(localDatabase, request.Envelope, runtimeClock);
        if (GrainControlledReadCapabilities.Handles(kind))
        {
            return GrainControlledReadCapabilities.Execute(localDatabase, services, runtimeClock,
                principal, request, cancellationToken);
        }
        if (kind == GrainReadKind.ClusterBackupOwner)
        {
            return await ClusterBackupOwnerReadExecution.ExecuteAsync(Administration, principal, request.Payload,
                services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseLimits>>(), runtimeClock,
                request.Envelope.ExpiresAt, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionMovementTransferData)
        {
            var execution = new PartitionMovementTransferDataObservedExecution(
                services.GetRequiredService<INativePartitionMovementTransferRead>(),
                services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseLimits>>(), runtimeClock, codec);
            return await execution.ExecuteAsync(principal, request, context,
                cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionMovementCapture)
        {
            return await PartitionMovementCaptureExecution.ExecuteAsync(
                services.GetRequiredService<INativePartitionMovementCapture>(), principal,
                GrainNativePayload.Read<PartitionMovementCaptureCapability>(request.Payload),
                cancellationToken).ConfigureAwait(true);
        }
        if (GrainNativeMaintenanceReadCapabilities.Handles(kind))
        {
            return await GrainNativeMaintenanceReadCapabilities.ExecuteAsync(services, principal,
                kind, request, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.Document
            && services.GetService<IRemoteDocumentReadRouter>() is { } remoteDocuments)
        {
            return await remoteDocuments.ReadAsync(request.Envelope, principal,
                GrainNativePayload.Read<GetDocumentRequest>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionQuery
            && services.GetService<IRemotePartitionQueryRouter>() is { } remoteQueries)
        {
            return await remoteQueries.ReadAsync(request.Envelope, principal,
                GrainNativePayload.ReadPublicInput<PartitionQueryRequestV1>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        return await ReadNativeCapabilityAsync(kind, principal, request, requestId, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadNativeCapabilityAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, CancellationToken cancellationToken)
    {
        if (kind == GrainReadKind.DistributedSearch)
        {
            return await DistributedSearchReadCapability.ExecuteAsync(services, principal, request, requestId,
                codec, context, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.DistributedSearchLeaf)
        {
            return DistributedSearchLeafCapability.Execute(localDatabase, services, runtimeClock,
                principal, request, cancellationToken);
        }
        if (RuntimeJournalRequestScope.Handles(kind))
        {
            return RuntimeJournalReadCapabilities.Execute(localDatabase, principal.Id, kind, request.Payload, cancellationToken);
        }
        if (GrainAdminDashboardCapabilities.Handles(kind))
        {
            return await GrainAdminDashboardCapabilities.ExecuteAsync(localDatabase, Administration, principal,
                kind, request.Payload, cancellationToken).ConfigureAwait(true);
        }
        if (GrainBlobReadCapabilities.Handles(kind))
        {
            return await blobs.ExecuteAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
        }
        if (GrainCoreReadCapabilities.Handles(kind))
        {
            return core.Execute(kind, principal.Id, request.Payload, cancellationToken);
        }

        if (kind is not (GrainReadKind.Backup or GrainReadKind.Admission or GrainReadKind.NodeStatus))
        {
            using var observation = codec.HasPhaseObserver
                ? NativeTextPostingObservation.Enter(token => codec.ObservePhaseAsync(request,
                    GrainRequestPhase.NativeTextOriginalPostingRead, context, token)) : null;
            return await Query.ExecuteAsync(kind, principal.Id, request.Payload, cancellationToken).ConfigureAwait(true);
        }

        GrainNativePayload.RequireNoDto(request.Payload);
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Backup => await Administration.BackupAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.Admission => Administration.Admission(),
            GrainReadKind.NodeStatus => await Administration.StatusAsync(cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}

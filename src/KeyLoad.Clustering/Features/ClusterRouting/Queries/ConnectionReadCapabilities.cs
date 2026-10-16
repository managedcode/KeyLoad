using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

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

        var principal = await ReloadPrincipalAsync(request, requestId, cancellationToken).ConfigureAwait(true);
        if (kind is GrainReadKind.PlacementPolicy or GrainReadKind.PlacementPlan or GrainReadKind.PlacementIntent)
        {
            return await TenantPlacementReadExecution.ExecuteAsync(localDatabase, services, runtimeClock,
                principal, request, cancellationToken).ConfigureAwait(true);
        }
        if (GrainControlledReadCapabilities.Handles(kind))
        {
            return kind == GrainReadKind.PartitionMovementParentState && codec.HasPhaseObserver
                ? await MovementParentStateObservedRead.ExecuteAsync(localDatabase, services, runtimeClock,
                    principal, request, codec, context, cancellationToken).ConfigureAwait(true)
                : GrainControlledReadCapabilities.Execute(localDatabase, services, runtimeClock,
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
                services.GetRequiredService<Func<INativePartitionMovementCapture>>()(), principal,
                GrainNativePayload.Read<PartitionMovementCaptureCapability>(request.Payload),
                cancellationToken).ConfigureAwait(true);
        }
        if (GrainNativeMaintenanceReadCapabilities.Handles(kind))
        {
            return await GrainNativeMaintenanceReadCapabilities.ExecuteAsync(services, principal,
                kind, request, cancellationToken).ConfigureAwait(true);
        }
        return await ReadRemoteOrNativeCapabilityAsync(kind, principal, request, requestId,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadRemoteOrNativeCapabilityAsync(GrainReadKind kind,
        PrincipalRecord principal, DecodedGrainRequest request, Guid requestId, CancellationToken cancellationToken)
    {
        if (kind == GrainReadKind.Document
            && services.GetService<IRemoteDocumentReadRouter>() is { } remoteDocuments)
        {
            return await remoteDocuments.ReadAsync(request.Envelope, principal,
                GrainNativePayload.Read<GetDocumentRequest>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind is GrainReadKind.WaitForAnnIndex or GrainReadKind.ApproximateSearch
            && services.GetService<IRemoteAnnReadRouter>() is { } remoteAnn)
        {
            return await remoteAnn.ReadAnnAsync(request.Envelope, principal, kind, request.Payload,
                services.GetRequiredService<SearchEngine>(), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionQuery
            && services.GetService<IRemotePartitionQueryRouter>() is { } remoteQueries)
        {
            return await remoteQueries.ReadAsync(request.Envelope, principal,
                GrainNativePayload.ReadPublicInput<PartitionQueryRequestV1>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        return await ReadNativeCapabilityAsync(kind, principal, request, requestId, cancellationToken).ConfigureAwait(true);
    }

    private async Task<PrincipalRecord> ReloadPrincipalAsync(DecodedGrainRequest request, Guid requestId,
        CancellationToken cancellationToken)
    {
        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.AuthorizationReload,
                context, cancellationToken).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, requestId, cancellationToken);
        }
        return GrainRequestAuthority.ReloadForRequest(localDatabase, request.Envelope, runtimeClock);
    }

    private async Task<object?> ReadNativeCapabilityAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, CancellationToken cancellationToken)
    {
        if (kind == GrainReadKind.AnnProjectionObservation)
        {
            return await AnnProjectionObservationRead.ExecuteAsync(localDatabase, services, runtimeClock,
                principal, request, cancellationToken).ConfigureAwait(true);
        }
        if (RemoteGraphRequestScope.HandlesRead(kind))
        {
            return GrainRemoteGraphReadCapabilities.Execute(localDatabase, services, runtimeClock, principal, request, cancellationToken);
        }
        if (EventVectorRequestScope.HandlesRead(kind))
        {
            return EventVectorReadCapabilities.Execute(localDatabase, services, principal, request, cancellationToken);
        }
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
                kind, request, codec, runtimeClock, cancellationToken).ConfigureAwait(true);
        }
        if (GrainBlobReadCapabilities.Handles(kind))
        {
            return await blobs.ExecuteAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
        }
        if (ConnectionRemoteTransferReadCapabilities.HandlesPeer(kind, request.Envelope))
        {
            return ConnectionRemoteTransferReadCapabilities.ExecutePeer(localDatabase, services,
                runtimeClock, principal, request, cancellationToken);
        }
        if (kind == GrainReadKind.QueueTransferReceipt)
        {
            var result = await ConnectionRemoteTransferReadCapabilities.TryReceiptAsync(services,
                principal, request, cancellationToken).ConfigureAwait(true);
            if (result.Routed)
            {
                return result.Receipt;
            }
        }
        if (GrainCoreReadCapabilities.Handles(kind))
        {
            return core.Execute(kind, principal.Id, request.Payload, cancellationToken,
                kind == GrainReadKind.Stream ? services.GetRequiredService<PhysicalShardRecord>() : null);
        }

        return await ReadQueryOrAdministrationAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadQueryOrAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        if (kind is not (GrainReadKind.Backup or GrainReadKind.Admission or GrainReadKind.NodeStatus))
        {
            using var observation = codec.HasPhaseObserver
                ? NativeTextPostingObservation.Enter(token => codec.ObservePhaseAsync(request,
                    GrainRequestPhase.NativeTextOriginalPostingRead, context, token)) : null;
            return await Query.ExecuteAsync(kind, principal.Id, request.Payload, cancellationToken).ConfigureAwait(true);
        }

        return await ReadAdministrationAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        GrainNativePayload.RequireNoDto(request.Payload);
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Backup => await Administration.BackupAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.Admission => Administration.Admission(),
            GrainReadKind.NodeStatus => await ReadNodeStatusAsync(request, cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }

    private async Task<NodeStatus> ReadNodeStatusAsync(DecodedGrainRequest request, CancellationToken token)
    {
        var result = await Administration.StatusAsync(token).ConfigureAwait(true);
        if (codec.HasPhaseObserver)
        {
            ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, token);
            GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(localDatabase,
                request.Envelope, runtimeClock));
        }
        return result;
    }

}

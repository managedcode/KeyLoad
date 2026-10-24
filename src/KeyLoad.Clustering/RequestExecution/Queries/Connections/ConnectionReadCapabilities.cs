using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.RuntimeJournaling.Identity;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Owns one operation's authorized capability dispatch and temporary read helpers.</summary>
internal sealed class ConnectionReadCapabilities(DatabaseEngine localDatabase, IServiceProvider services,
    TimeProvider runtimeClock, GrainRequestCodec codec, IGrainContext context)
{
    private readonly GrainCoreReadCapabilities core = new(localDatabase);
    private GrainQueryReadCapabilities? query;
    private GrainQueryReadCapabilities Query => query ??= new(services.GetRequiredService<QueryEngine>(),
        services.GetRequiredService<SearchEngine>(), runtimeClock);
    private ISiloDatabaseService Administration => services.GetRequiredService<SiloDatabaseClient>()
        .On(GrainIdentityContext.RequireOriginSilo());

    internal async Task<object?> ReadAsync(DecodedGrainRequest request, Guid requestId,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.Authenticate)
        {
            return request.Envelope.Purpose == AdministratorAuthenticationProtocol.Purpose
                ? AdministratorAuthenticationRead.Execute(localDatabase, runtimeClock, codec.ExecutionOptions,
                    request, binding, cancellationToken)
                : GrainRequestAuthority.Authenticate(localDatabase, request.Payload, runtimeClock);
        }

        if (kind == GrainReadKind.RuntimeJournalCatalog
            && request.Envelope.Purpose == GrainContracts.RuntimeJournalPurpose
            && request.Envelope.PrincipalId == RuntimeJournalIdentity.ProtectedPrincipalId
            && localDatabase.Store.Read(view => view.GetRecord<PrincipalRecord>(
                KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId))) is null)
        {
            GrainPayload.RequireNoDto(request.Payload);
            return null;
        }

        var principal = await ReloadPrincipalAsync(request, requestId, null, cancellationToken).ConfigureAwait(true);
        if (kind == GrainReadKind.FileMetadata)
        {
            return localDatabase.ReadFileMetadataBound(principal.Id,
                GrainPayload.Read<global::KeyLoad.Core.BlobStorage.FileMetadataQuery>(request.Payload),
                binding, cancellationToken);
        }
        if (UserAnalyticsReadExecution.Handles(kind))
        {
            return await UserAnalyticsReadExecution.ExecuteAsync(localDatabase, services, principal, kind, request,
                binding, cancellationToken).ConfigureAwait(true);
        }
        if (GrainLifetimeReadCapabilities.Handles(kind))
        {
            return GrainLifetimeReadCapabilities.Execute(localDatabase, services, runtimeClock,
                principal, request, binding, cancellationToken);
        }
        if (GrainMaintenanceReadCapabilities.Handles(kind))
        {
            return await GrainMaintenanceReadCapabilities.ExecuteAsync(services, principal,
                kind, request, binding, cancellationToken).ConfigureAwait(true);
        }
        return await ReadCapabilityAsync(kind, principal, request, requestId,
            binding, cancellationToken).ConfigureAwait(true);
    }

    private async Task<PrincipalRecord> ReloadPrincipalAsync(DecodedGrainRequest request, Guid requestId,
        ReadExecutionBudget? work, CancellationToken cancellationToken)
    {
        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.AuthorizationReload,
                context, cancellationToken).ConfigureAwait(true);
            ConnectionReadExecution.ValidateFreshRequest(codec, request, requestId, cancellationToken);
        }
        return work is null
            ? GrainRequestAuthority.ReloadForRequest(localDatabase, request.Envelope, runtimeClock)
            : GrainRequestAuthority.ReloadForRequest(localDatabase, request.Envelope, runtimeClock, work);
    }

    private async Task<object?> ReadCapabilityAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, IResourceLifetimeWorkBinding binding,
        CancellationToken cancellationToken)
    {
        if (kind == GrainReadKind.AnnProjectionObservation)
        {
            return await AnnProjectionObservationRead.ExecuteAsync(localDatabase, services, runtimeClock,
                principal, request, binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.DistributedSearch)
        {
            return await DistributedSearchReadCapability.ExecuteAsync(localDatabase, services, principal, request,
                requestId, codec, context, runtimeClock, binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionQuery)
        {
            return await PartitionQueryReadCapability.ExecuteAsync(localDatabase, services, principal, request,
                codec, runtimeClock, binding, cancellationToken).ConfigureAwait(true);
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
        if (GrainCoreReadCapabilities.Handles(kind))
        {
            var budget = new ReadExecutionBudget(localDatabase.OperationLimitsOptions,
                runtimeClock, cancellationToken);
            budget.ConstrainLifetime(request.Envelope.ExpiresAt);
            return core.ExecuteBound(kind, principal.Id, request.Payload, budget, binding,
                kind == GrainReadKind.Stream ? services.GetRequiredService<PhysicalShardRecord>() : null);
        }

        return await ReadQueryOrAdministrationAsync(kind, principal, request, binding, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadQueryOrAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        if (kind is not (GrainReadKind.Backup or GrainReadKind.Admission or GrainReadKind.NodeStatus))
        {
            using var observation = codec.HasPhaseObserver
                ? TextPostingObservation.Enter(token => codec.ObservePhaseAsync(request,
                    GrainRequestPhase.TextOriginalPostingRead, context, token)) : null;
            return await Query.ExecuteAsync(kind, principal.Id, request.Payload, binding, cancellationToken).ConfigureAwait(true);
        }

        return await ReadAdministrationAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        GrainPayload.RequireNoDto(request.Payload);
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        object result = kind switch
        {
            GrainReadKind.Backup => await Administration.CreateBackupAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.Admission => await Administration.ReadAdmissionAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.NodeStatus => await Administration.ReadStatusAsync(cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
        ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, cancellationToken);
        GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(localDatabase,
            request.Envelope, runtimeClock));
        return result;
    }

}

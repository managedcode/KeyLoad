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
    private GrainQueryReadCapabilities? query;
    private GrainQueryReadCapabilities Query => query ??= new(services.GetRequiredService<QueryEngine>(),
        services.GetRequiredService<SearchEngine>(), runtimeClock);
    private ISiloDatabaseService Administration => services.GetRequiredService<SiloDatabaseClient>()
        .On(GrainIdentityContext.RequireOriginSilo());

    internal async Task<GrainValue> ReadAsync(DecodedGrainRequest request, Guid requestId,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: BCL null validation accepts the binding IResourceLifetimeWorkBinding reference through its object parameter. */;
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.Authenticate)
        {
            return request.Envelope.Purpose == AdministratorAuthenticationProtocol.Purpose
                ? (new GrainValue(AdministratorAuthenticationRead.Execute(localDatabase, runtimeClock,
                    request, binding, cancellationToken)) /* KLD0041: This concrete authorized response enters the existing generated GrainValue slot with its original native runtime type header. */)
                : (new GrainValue(GrainRequestAuthority.Authenticate(localDatabase, (GrainPayload.Read<string>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as string at this closed operation boundary before executing typed code. */), runtimeClock)) /* KLD0041: This concrete authorized response enters the existing generated GrainValue slot with its original native runtime type header. */);
        }

        if (kind == GrainReadKind.RuntimeJournalCatalog
            && request.Envelope.Purpose == GrainContracts.RuntimeJournalPurpose
            && request.Envelope.PrincipalId == RuntimeJournalIdentity.ProtectedPrincipalId
            && localDatabase.Store.Read(view => view.GetRecord<PrincipalRecord>(
                KeySpace.Principal(RuntimeJournalIdentity.ProtectedPrincipalId))) is null)
        {
            GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
            return (new GrainValue(null) /* KLD0041: This concrete authorized response enters the existing generated GrainValue slot with its original native runtime type header. */);
        }

        var principal = await ReloadPrincipalAsync(request, requestId, null, cancellationToken).ConfigureAwait(true);
        if (kind == GrainReadKind.CurrentPrincipal)
        {
            GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
            return (new GrainValue(principal) /* KLD0041: This concrete authorized response enters the existing generated GrainValue slot with its original native runtime type header. */);
        }
        if (kind == GrainReadKind.FileMetadata)
        {
            return (new GrainValue(localDatabase.ReadFileMetadataBound(principal.Id,
                (GrainPayload.Read<global::KeyLoad.Core.BlobStorage.FileMetadataQuery>((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the received generated envelope payload as KeyLoad.Core.BlobStorage.FileMetadataQuery at this closed operation boundary before executing typed code. */),
                binding, cancellationToken)) /* KLD0041: This concrete authorized response enters the existing generated GrainValue slot with its original native runtime type header. */);
        }
        if (UserAnalyticsReadExecution.Handles(kind))
        {
            return await UserAnalyticsReadExecution.ExecuteAsync(localDatabase, services, principal, kind, request,
                binding, cancellationToken).ConfigureAwait(true);
        }
        if (kind is GrainReadKind.PartitionQueryLeaf or GrainReadKind.DistributedSearchLeaf)
        {
            return QueryLifetimeReadCapability.Execute(localDatabase, services, runtimeClock,
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

    private async Task<GrainValue> ReadCapabilityAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, Guid requestId, IResourceLifetimeWorkBinding binding,
        CancellationToken cancellationToken)
    {
        if (kind == GrainReadKind.AnnProjectionObservation)
        {
            return new GrainValue(await AnnProjectionObservationRead.ExecuteAsync(localDatabase, services, runtimeClock,
                principal, request, binding, cancellationToken).ConfigureAwait(true)) /* KLD0041: The current GrainValue wire slot carries this AnnProjectionObservation response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.DistributedSearch)
        {
            return new GrainValue(await DistributedSearchReadCapability.ExecuteAsync(localDatabase, services, principal, request,
                requestId, codec, context, runtimeClock, binding, cancellationToken).ConfigureAwait(true)) /* KLD0041: The current GrainValue wire slot carries this DistributedSearch response with its generated runtime type header. */;
        }
        if (kind == GrainReadKind.PartitionQuery)
        {
            return new GrainValue(await PartitionQueryReadCapability.ExecuteAsync(localDatabase, services, principal, request,
                codec, runtimeClock, binding, cancellationToken).ConfigureAwait(true)) /* KLD0041: The current GrainValue wire slot carries this PartitionQuery response with its generated runtime type header. */;
        }
        if (RuntimeJournalRequestScope.Handles(kind))
        {
            return RuntimeJournalReadCapabilities.Execute(localDatabase, principal.Id, kind, request, cancellationToken);
        }
        if (GrainAdminDashboardCapabilities.Handles(kind))
        {
            return await GrainAdminDashboardCapabilities.ExecuteAsync(localDatabase, Administration, principal,
                kind, request, codec, runtimeClock, cancellationToken).ConfigureAwait(true);
        }
        if (GrainCoreReadCapabilities.Handles(kind))
        {
            var budget = new ReadExecutionBudget(runtimeClock, cancellationToken);
            return GrainCoreReadCapabilities.Execute(localDatabase, kind, principal.Id, request, budget, binding,
                kind == GrainReadKind.Stream ? services.GetRequiredService<PhysicalShardRecord>() : null);
        }

        return await ReadQueryOrAdministrationAsync(kind, principal, request, binding, cancellationToken).ConfigureAwait(true);
    }

    private async Task<GrainValue> ReadQueryOrAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        if (kind is not (GrainReadKind.Backup or GrainReadKind.NodeStatus))
        {
            using var observation = codec.HasPhaseObserver
                ? TextPostingObservation.Enter(token => codec.ObservePhaseAsync(request,
                    GrainRequestPhase.TextOriginalPostingRead, context, token)) : null;
            return await Query.ExecuteAsync(kind, principal.Id, request, binding, cancellationToken).ConfigureAwait(true);
        }

        return await ReadAdministrationAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<GrainValue> ReadAdministrationAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        GrainPayload.RequireNoDto((request.Payload /* KLD0041: Read this verified native envelope Payload slot only for the selected closed-kind handler to validate its concrete request type. */)) /* KLD0041: Validate the current generated envelope scalar NoDto marker for this capability before executing a request without a DTO. */;
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        var result = kind switch
        {
            GrainReadKind.Backup => new GrainValue(await Administration.CreateBackupAsync(cancellationToken).ConfigureAwait(true)) /* KLD0041: The current GrainValue wire slot carries this Backup response with its generated runtime type header. */,
            GrainReadKind.NodeStatus => new GrainValue(await Administration.ReadStatusAsync(cancellationToken).ConfigureAwait(true)) /* KLD0041: The current GrainValue wire slot carries this NodeStatus response with its generated runtime type header. */,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
        ConnectionReadExecution.ValidateFreshRequest(codec, request, request.Envelope.RequestId, cancellationToken);
        GrainRequestAuthority.RequireAdministrator(GrainRequestAuthority.ReloadForRequest(localDatabase,
            request.Envelope, runtimeClock));
        return result;
    }

}

using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string RemoteAnnReadInvalid = "The native ANN remote read scope is no longer current.";

    internal RemoteAnnReadFence? CaptureRemoteAnnRead(string principalId, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation,
        RegisteredPhysicalOwnerV1 configuredLocal, ReadExecutionBudget budget)
        => Store.Read(view => CaptureRemoteAnnRead(view, principalId, consumer, collection, field, space,
            generation, configuredLocal, budget));

    private RemoteAnnReadFence? CaptureRemoteAnnRead(IKeyValueView view, string principalId,
        ProjectionConsumerRef consumer, string collection, string field, VectorSpace space,
        long generation, RegisteredPhysicalOwnerV1 configuredLocal, ReadExecutionBudget budget,
        Action<PrincipalRecord, ResourceDefinition>? capture = null)
    {
        budget.Check();
        RequireConfiguredRemoteAnnOwner(configuredLocal);
        ValidatePartition(consumer.Partition);
        JsonData.Identifier(collection);
        var bounded = budget.CreateView(view);
        var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
        if (!principal.ClusterAdministrator && principal.TenantId != consumer.Partition.TenantId)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, RemoteAnnReadInvalid);
        }
        Authorization.Require(principal, consumer.Partition, collection, Capability.VectorSearch);
        // This is source metadata authorization only; no local resource/data read is authorized here.
        // The addressed receiver independently requires actual local ownership and current field policy.
        var resource = ReadRemoteAnnResource(consumer, collection, field, bounded, principal, capture is not null);
        capture?.Invoke(principal, resource);
        var configured = ProjectionConsumer(bounded, consumer);
        if (configured.Definition.AnnMaintenance is null)
        {
            return null;
        }
        var state = ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, resource);
        var original = state.InitialAnnMaintenance;
        if (!state.InitialAnnMaintenanceSettled || original is null
            || original.Consumer != consumer || original.IndexGeneration != generation
            || original.Collection != collection || original.Field != field || original.Space != space
            || original.NodeId == Guid.Empty || original.MaintenanceProfile != state.Definition.AnnMaintenance)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, RemoteAnnReadInvalid);
        }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (configuredPhysicalOwner is not null
            && !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        }
        var placement = ResolveRegisteredPlacement(consumer.Partition, catalog.DefaultShard);
        if (!PhysicalOwnerEntryValidation.SameOwner(original.Placement,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        }
        var localPlacement = placement.PhysicalShardId == configuredLocal.Owner.PhysicalShardId;
        var directory = localPlacement ? null : PhysicalOwnerDirectorySerialization.Read(bounded)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        var destination = localPlacement ? configuredLocal : directory!.Owners.SingleOrDefault(entry =>
            entry.Owner.PhysicalShardId == placement.PhysicalShardId)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        if (destination.Owner.PhysicalShardId != original.Placement.PhysicalShardId
            || destination.Owner.Incarnation != original.Placement.Incarnation
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        }
        var identity = Store.Identity;
        return new(principal.Id, principal.TenantId, principal.PolicyEpoch, identity.NodeId,
            identity.ReadGeneration, directory?.Revision, RemoteAnnDigest(resource, budget),
            RemoteAnnDigest(state, budget), configuredLocal, destination, original);
    }

    private void RequireConfiguredRemoteAnnOwner(RegisteredPhysicalOwnerV1 configuredLocal)
    {
        if (!PhysicalOwnerEntryValidation.Valid(configuredLocal) || configuredPhysicalOwner is null
                    || !PhysicalOwnerEntryValidation.SameOwner(configuredLocal.Owner, configuredPhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteAnnReadInvalid);
        }
    }

    private ResourceDefinition ReadRemoteAnnResource(ProjectionConsumerRef consumer, string collection, string field,
        IKeyValueView bounded, PrincipalRecord principal, bool lifetimeCapture)
    {
        var resource = bounded.GetRecord<ResourceDefinition>(KeySpace.Resource(
            consumer.Partition.TenantId, consumer.Partition.DatabaseId, collection))
            ?? throw Errors.Fail(ErrorCode.NotFound, RemoteAnnReadInvalid);
        if (resource.TransactionDomainId != consumer.Partition.TransactionDomainId
            || resource.Kind != ResourceKind.Collection || resource.Name != collection)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteAnnReadInvalid);
        }
        if (lifetimeCapture)
        {
            AnnLifetimeReadAuthority.RequireSchema(this, principal, resource, field);
        }
        else
        {
            Authorization.RequireFieldUse(principal, resource, field);
        }
        return resource;
    }

    internal void ValidateRemoteAnnRead(RemoteAnnReadFence original, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation, ReadExecutionBudget budget)
    {
        var current = CaptureRemoteAnnRead(original.PrincipalId, consumer, collection, field, space,
            generation, original.ConfiguredLocal, budget);
        if (current is null || current.SourceNodeId != original.SourceNodeId
            || current.SourceReadGeneration != original.SourceReadGeneration
            || current.PolicyEpoch != original.PolicyEpoch || current.TenantId != original.TenantId
            || current.DirectoryRevision != original.DirectoryRevision
            || current.ResourceSha256 != original.ResourceSha256
            || current.ConsumerSha256 != original.ConsumerSha256
            || !PhysicalOwnerEntryValidation.Same(current.Destination, original.Destination))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteAnnReadInvalid);
        }
    }

    private static string RemoteAnnDigest<T>(T value, ReadExecutionBudget budget)
    {
        var measured = NativeSerialization.Measure(value);
        budget.ChargeBytes(measured);
        var bytes = NativeSerialization.Serialize(value);
        try
        {
            if (bytes.Length != measured)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteAnnReadInvalid);
            }
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

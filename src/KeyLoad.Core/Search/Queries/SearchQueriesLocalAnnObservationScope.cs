using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Core;
/// <summary>Executes SearchQueriesLocalAnnObservationScope operations against the caller's database context.</summary>
public static class SearchQueriesLocalAnnObservationScope
{
    internal const string LocalAnnObservationInvalid = "The execution ANN observation scope is no longer current.";
    internal static LocalAnnObservationFence CaptureLocalAnnObservation(this DatabaseEngine engine, string principalId, AnnMaintenanceRequest original, ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata = null) => engine.CaptureLocalAnnObservationCore(principalId, original, budget, sourceMetadata, bindResourceLifetime: false);
    internal static LocalAnnObservationFence CaptureLocalAnnObservationBound(this DatabaseEngine engine, string principalId, AnnMaintenanceRequest original, ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.CaptureLocalAnnObservationCore(principalId, original, budget, sourceMetadata, bindResourceLifetime: true);
    }

    internal static LocalAnnObservationFence CaptureLocalAnnObservationCore(this DatabaseEngine engine, string principalId, AnnMaintenanceRequest original, ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata, bool bindResourceLifetime)
    {
        return engine.Store.Read(view =>
        {
            budget.Check();
            var bounded = budget.CreateView(view);
            var principal = engine.Principal(bounded, principalId, engine.EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator && principal.TenantId != original.Consumer.Partition.TenantId)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, LocalAnnObservationInvalid);
            }

            engine.Authorization.Require(principal, original.Consumer.Partition, original.Collection, Capability.VectorSearch);
            var resource = bindResourceLifetime ? ResourceExecutionQueriesResourceLifetimeReadBinding.BindAuthorizedReadResource(bounded, original.Consumer.Partition, original.Collection, ResourceKind.Collection) : engine.Resource(bounded, original.Consumer.Partition, original.Collection, ResourceKind.Collection);
            if (sourceMetadata is null)
            {
                engine.Authorization.RequireFieldUse(principal, resource, original.Field);
            }
            else
            {
                AnnLifetimeReadAuthority.Require(engine, view, principal, resource, original.Consumer.Partition, original.Collection, original.Field, sourceMetadata, budget);
            }

            var current = engine.ReadPlacementWitness(bounded, original.Consumer.Partition);
            if (current.PhysicalShardId != original.Placement.PhysicalShardId || current.Incarnation != original.Placement.Incarnation)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, LocalAnnObservationInvalid);
            }

            var state = engine.ReadProjectionMaintenanceEligibilityCore(view, original.Consumer, original.IndexGeneration, budget, null);
            if (!state.InitialAnnMaintenanceSettled || state.InitialAnnMaintenance is not { } retained)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, LocalAnnObservationInvalid);
            }

            RequireSameAnnObservation(retained, original, budget);
            var actual = engine.Store.Identity;
            return new LocalAnnObservationFence(principal.Id, principal.TenantId, principal.PolicyEpoch, actual.NodeId, actual.ReadGeneration, LocalAnnObservationDigest(resource, budget), LocalAnnObservationDigest(state, budget));
        });
    }

    internal static void RequireLocalAnnObservation(this DatabaseEngine engine, string principalId, AnnMaintenanceRequest original, LocalAnnObservationFence admitted, ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata = null)
    {
        if (engine.CaptureLocalAnnObservation(principalId, original, budget, sourceMetadata) != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, LocalAnnObservationInvalid);
        }
    }

    internal static void RequireSameAnnObservation(AnnMaintenanceRequest retained, AnnMaintenanceRequest original, ReadExecutionBudget budget)
    {
        var retainedSize = OrleansSerialization.Measure(retained);
        var originalSize = OrleansSerialization.Measure(original);
        budget.ChargeBytes(checked(retainedSize + originalSize));
        var retainedBytes = OrleansSerialization.Serialize(retained);
        byte[]? originalBytes = null;
        try
        {
            originalBytes = OrleansSerialization.Serialize(original);
            if (retainedBytes.Length != retainedSize || originalBytes.Length != originalSize || !retainedBytes.AsSpan().SequenceEqual(originalBytes))
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, LocalAnnObservationInvalid);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(retainedBytes);
            if (originalBytes is not null)
            {
                CryptographicOperations.ZeroMemory(originalBytes);
            }
        }
    }

    internal static string LocalAnnObservationDigest<T>(T value, ReadExecutionBudget budget)
    {
        var measured = OrleansSerialization.Measure(value);
        budget.ChargeBytes(measured);
        var bytes = OrleansSerialization.Serialize(value);
        try
        {
            if (bytes.Length != measured)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, LocalAnnObservationInvalid);
            }

            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

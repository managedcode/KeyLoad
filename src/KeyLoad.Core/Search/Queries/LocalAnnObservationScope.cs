using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string LocalAnnObservationInvalid = "The execution ANN observation scope is no longer current.";

    internal LocalAnnObservationFence CaptureLocalAnnObservation(string principalId, AnnMaintenanceRequest original,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata = null)
        => CaptureLocalAnnObservationCore(principalId, original, budget, sourceMetadata, bindResourceLifetime: false);

    internal LocalAnnObservationFence CaptureLocalAnnObservationBound(string principalId, AnnMaintenanceRequest original,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return CaptureLocalAnnObservationCore(principalId, original, budget, sourceMetadata, bindResourceLifetime: true);
    }

    private LocalAnnObservationFence CaptureLocalAnnObservationCore(string principalId, AnnMaintenanceRequest original,
        ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata, bool bindResourceLifetime)
    {
        return Store.Read(view =>
        {
            budget.Check();
            var bounded = budget.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            if (!principal.ClusterAdministrator && principal.TenantId != original.Consumer.Partition.TenantId)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, LocalAnnObservationInvalid);
            }
            Authorization.Require(principal, original.Consumer.Partition, original.Collection, Capability.VectorSearch);
            var resource = bindResourceLifetime
                ? BindAuthorizedReadResource(bounded, original.Consumer.Partition, original.Collection, ResourceKind.Collection)
                : Resource(bounded, original.Consumer.Partition, original.Collection, ResourceKind.Collection);
            if (sourceMetadata is null)
            {
                Authorization.RequireFieldUse(principal, resource, original.Field);
            }
            else
            {
                AnnLifetimeReadAuthority.Require(this, view, principal, resource, original.Consumer.Partition,
                    original.Collection, original.Field, sourceMetadata, budget);
            }
            var current = ReadPlacementWitness(bounded, original.Consumer.Partition);
            if (current.PhysicalShardId != original.Placement.PhysicalShardId
                || current.Incarnation != original.Placement.Incarnation)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, LocalAnnObservationInvalid);
            }
            var state = ReadProjectionMaintenanceEligibilityCore(view, original.Consumer, original.IndexGeneration, budget, null);
            if (!state.InitialAnnMaintenanceSettled || state.InitialAnnMaintenance is not { } retained)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, LocalAnnObservationInvalid);
            }
            RequireSameAnnObservation(retained, original, budget);
            var actual = Store.Identity;
            return new LocalAnnObservationFence(principal.Id, principal.TenantId, principal.PolicyEpoch,
                actual.NodeId, actual.ReadGeneration, LocalAnnObservationDigest(resource, budget), LocalAnnObservationDigest(state, budget));
        });
    }

    internal void RequireLocalAnnObservation(string principalId, AnnMaintenanceRequest original,
        LocalAnnObservationFence admitted, ReadExecutionBudget budget, AnnReadLifetimeMetadata? sourceMetadata = null)
    {
        if (CaptureLocalAnnObservation(principalId, original, budget, sourceMetadata) != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, LocalAnnObservationInvalid);
        }
    }

    private static void RequireSameAnnObservation(AnnMaintenanceRequest retained,
        AnnMaintenanceRequest original, ReadExecutionBudget budget)
    {
        var retainedSize = OrleansSerialization.Measure(retained);
        var originalSize = OrleansSerialization.Measure(original);
        budget.ChargeBytes(checked(retainedSize + originalSize));
        var retainedBytes = OrleansSerialization.Serialize(retained);
        byte[]? originalBytes = null;
        try
        {
            originalBytes = OrleansSerialization.Serialize(original);
            if (retainedBytes.Length != retainedSize || originalBytes.Length != originalSize
                || !retainedBytes.AsSpan().SequenceEqual(originalBytes))
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
    private static string LocalAnnObservationDigest<T>(T value, ReadExecutionBudget budget)
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

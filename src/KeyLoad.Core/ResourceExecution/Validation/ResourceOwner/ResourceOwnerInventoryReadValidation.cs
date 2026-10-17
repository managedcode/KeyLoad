using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Validates the bounded internal inventory envelope without creating authority.</summary>
internal static class ResourceOwnerInventoryReadValidation
{
    internal const int Version = 1;
    internal const long EmptyRevision = 0;
    private const int DigestCharacters = 64;
    internal const string Invalid = "The resource owner inventory request or cursor is invalid.";
    internal const string Changed = "The resource owner inventory observation changed.";
    internal const string Missing = "The resource owner inventory authority is not proven.";
    internal const string Malformed = "The resource owner inventory metadata is malformed.";
    internal const string Expired = "The resource owner inventory deadline expired.";
    internal const string Capacity = "The resource owner inventory exceeds its native bound.";
    internal const string Administrator = "Resource owner inventory requires current cluster administration.";

    internal static void Request(ResourceOwnerInventoryRequestV1 request, DatabaseLimits limits)
    {
        if (request is null || request.Version != Version
            || request.ExpectedIncarnation?.Value is not { Length: DigestCharacters } digest
            || !digest.All(char.IsAsciiHexDigitLower))
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        Fields(request.InventoryId, request.TenantId, request.DatabaseId, request.ResourceName, request.PageSize, limits);
        Bound(request, limits);
    }

    internal static void Fields(Guid inventoryId, string tenantId, string databaseId, string resourceName,
        int pageSize, DatabaseLimits limits)
    {
        if (inventoryId == Guid.Empty || pageSize <= EmptyRevision
            || pageSize > limits.MaxResults || pageSize > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        JsonData.Identifier(tenantId);
        JsonData.Identifier(databaseId);
        JsonData.Identifier(resourceName);
    }

    internal static void Control(ResourceOwnerInventoryControlV1 control, DatabaseLimits limits)
    {
        if (control is null || control.Definition is null || control.Lifetime is null
            || control.Catalog is null || control.Directory is null || control.PolicyEpoch < EmptyRevision
            || control.PlacementDirectoryRevision < EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        Request(control.Request, limits);
        JsonData.Identifier(control.PrincipalId);
        ResourceLifetimeIdentity.Validate(control.Lifetime, control.Request.TenantId,
            control.Request.DatabaseId, control.Definition);
        if (control.Definition.Name != control.Request.ResourceName
            || control.Lifetime.Incarnation != control.Request.ExpectedIncarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Changed);
        }
        PhysicalShardCatalogValidation.ValidateCatalog(control.Catalog);
        PhysicalOwnerDirectoryValidation.Validate(control.Directory);
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, control.Catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.Corruption, Malformed);
        }
        Bound(control, limits);
    }

    internal static void Expiry(DateTimeOffset expiresAt, DateTimeOffset now, ReadExecutionBudget work)
    {
        if (expiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, Expired);
        }
        work.ConstrainLifetime(expiresAt);
    }

    internal static void Bound<T>(T value, DatabaseLimits limits)
    {
        if (NativeSerialization.Measure(value) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
    }

    internal static bool Same<T>(T left, T right)
        => NativeSerialization.Serialize(left).AsSpan().SequenceEqual(NativeSerialization.Serialize(right));

    internal static void OwnerCut(ResourceOwnerInventoryOwnerCutV1 cut, DatabaseLimits limits)
    {
        if (cut is null || cut.ObservedOwner is null || cut.NodeId == Guid.Empty || cut.StoreIncarnation == Guid.Empty
            || cut.ObservedOwner.PhysicalShardId == Guid.Empty || cut.ObservedOwner.Incarnation == Guid.Empty
            || cut.ObservedOwner.VoterIds.IsDefaultOrEmpty || cut.ReadGeneration < EmptyRevision
            || cut.WriterVersion != Version || cut.EnrollmentRevision < EmptyRevision || cut.CatalogRevision <= EmptyRevision
            || cut.OwnerDirectoryRevision < EmptyRevision || cut.PlacementDirectoryRevision < EmptyRevision
            || cut.StorePosition < EmptyRevision || cut.AppliedIndex < EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        Bound(cut, limits);
    }

    internal static void Cut(ResourceOwnerInventoryOwnerCutV1 expected, ResourceOwnerInventoryOwnerCutV1 current)
    {
        if (expected.NodeId != current.NodeId || expected.StoreIncarnation != current.StoreIncarnation
            || expected.ReadGeneration != current.ReadGeneration || expected.WriterVersion != current.WriterVersion)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Changed);
        }
        if (expected.EnrollmentRevision != current.EnrollmentRevision)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, Changed);
        }
        if (!PhysicalOwnerEntryValidation.SameOwner(expected.ObservedOwner, current.ObservedOwner)
            || expected.CatalogRevision != current.CatalogRevision
            || expected.OwnerDirectoryRevision != current.OwnerDirectoryRevision
            || expected.PlacementDirectoryRevision != current.PlacementDirectoryRevision)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Changed);
        }
        if (expected.StorePosition != current.StorePosition || expected.AppliedIndex != current.AppliedIndex)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Changed);
        }
    }
}

using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks frozen retirement intent, source identity and actual committed receipt shapes.</summary>
internal static class ResourceRetirementValidation
{
    internal static void Request(ResourceRetirementRequestV1 request, DatabaseLimits limits)
    {
        if (request is null || request.Version != ResourceRetirementProtocol.Version
            || request.RetirementId == Guid.Empty || request.ExpectedIncarnation is null
            || !Digest(request.ExpectedIncarnation.Value))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        JsonData.Identifier(request.ResourceName);
        Bound(request, limits);
    }

    internal static void Control(ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        if (control is null || control.Definition is null || control.Lifetime is null
            || control.Catalog is null || control.Directory is null || control.BeginReceipt is null
            || control.PolicyEpoch < ResourceRetirementProtocol.Empty
            || control.PlacementDirectoryRevision < ResourceRetirementProtocol.Empty || !Digest(control.IntentDigest))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        Request(control.Request, limits);
        JsonData.Identifier(control.PrincipalId);
        ResourceLifetimeIdentity.Validate(control.Lifetime, control.Request.TenantId,
            control.Request.DatabaseId, control.Definition);
        if (control.Request.ResourceName != control.Definition.Name
            || control.Request.ExpectedIncarnation != control.Lifetime.Incarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        PhysicalShardCatalogValidation.ValidateCatalog(control.Catalog);
        PhysicalOwnerDirectoryValidation.Validate(control.Directory);
        if (control.Directory.Owners.Length > limits.MaxResults
            || control.Directory.Owners.Sum(owner => owner.Owner.VoterIds.Length) > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Directory.ControlOwner, control.Catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementWorkOriginValidation.Optional(control, limits);
        Receipt(control.BeginReceipt, control, ResourceRetirementPhase.Begin, control.Catalog.DefaultShard, limits);
        Bound(control, limits);
    }

    internal static void Receipt(ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementControlV1 control,
        ResourceRetirementPhase phase, PhysicalShardRecord owner, DatabaseLimits limits, Guid attemptId = default)
    {
        var identityOwner = phase == ResourceRetirementPhase.RecordOwnerReceipt
            ? owner.PhysicalShardId : receipt?.PhysicalOwner?.PhysicalShardId ?? Guid.Empty;
        if (receipt is null || receipt.PhysicalOwner is null || receipt.Version != ResourceRetirementProtocol.Version
            || receipt.RetirementId != control.Request.RetirementId || receipt.Phase != phase
            || receipt.AppliedPosition <= ResourceRetirementProtocol.Empty
            || receipt.IntentDigest != control.IntentDigest || !Same(receipt.Lifetime, control.Lifetime)
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner)
            || receipt.CommandId != (phase is ResourceRetirementPhase.PrepareAttempt or ResourceRetirementPhase.SealBarrier or ResourceRetirementPhase.PreparedOperation
                ? ResourceRetirementIdentity.AttemptCommandId(control.Request, phase, attemptId, identityOwner)
                : ResourceRetirementIdentity.PhaseCommandId(control.Request, phase, identityOwner)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        Bound(receipt, limits);
    }

    internal static void Bound<T>(T value, DatabaseLimits limits)
    {
        if (NativeSerialization.Measure(value) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
    }

    internal static bool Same<T>(T left, T right)
        => ResourceOwnerInventoryReadValidation.Same(left, right);

    internal static bool Digest(string value)
        => value is { Length: ResourceRetirementProtocol.DigestCharacters } && value.All(char.IsAsciiHexDigitLower);
}

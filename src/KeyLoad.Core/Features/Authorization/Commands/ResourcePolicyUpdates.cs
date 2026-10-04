namespace KeyLoad.Core.Features.Authorization;

/// <summary>Validates the compare-and-set boundary for resource policy updates.</summary>
internal static class ResourcePolicyUpdates
{
    private const string MissingResourceMessage = "The resource does not exist at the expected schema version.";
    private const string StaleVersionMessage = "The expected resource schema version is stale.";
    private const string InvalidVersionMessage = "A policy update must increment the resource schema version exactly once.";
    private const string NoPolicyChangeMessage = "A policy update must change at least one field or header policy.";
    private const string PhysicalMigrationMessage = "Resource migrations require an explicit migration job.";
    private const string VersionExhaustedMessage = "The resource schema version cannot be incremented.";

    /// <summary>Validates a resource creation, legacy identical-definition request, or policy-only CAS.</summary>
    /// <param name="previous">The definition read from the current apply transaction, if any.</param>
    /// <param name="replacement">The complete replacement definition.</param>
    /// <param name="expectedVersion">The expected current version, or null for legacy semantics.</param>
    internal static void Validate(ResourceDefinition? previous, ResourceDefinition replacement, long? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (previous is null)
        {
            if (expectedVersion is not null)
            {
                throw Errors.Fail(ErrorCode.RevisionConflict, MissingResourceMessage);
            }
            return;
        }

        if (expectedVersion is null)
        {
            if (JsonData.Fingerprint(previous) != JsonData.Fingerprint(replacement))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalMigrationMessage);
            }
            return;
        }

        if (expectedVersion.Value != previous.SchemaVersion)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, StaleVersionMessage);
        }
        if (previous.SchemaVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, VersionExhaustedMessage);
        }
        if (replacement.SchemaVersion != previous.SchemaVersion + 1)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVersionMessage);
        }
        if (!SameNonPolicyDefinition(previous, replacement))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PhysicalMigrationMessage);
        }
        if (SamePolicies(previous, replacement))
        {
            throw Errors.Fail(ErrorCode.Validation, NoPolicyChangeMessage);
        }
    }

    private static bool SameNonPolicyDefinition(ResourceDefinition previous, ResourceDefinition replacement)
    {
        var previousShape = previous with { FieldPolicies = [], HeaderPolicies = [], SchemaVersion = 0 };
        var replacementShape = replacement with { FieldPolicies = [], HeaderPolicies = [], SchemaVersion = 0 };
        return JsonData.Fingerprint(previousShape) == JsonData.Fingerprint(replacementShape);
    }

    private static bool SamePolicies(ResourceDefinition previous, ResourceDefinition replacement)
        => JsonData.Fingerprint(previous.FieldPolicies) == JsonData.Fingerprint(replacement.FieldPolicies)
            && JsonData.Fingerprint(previous.HeaderPolicies) == JsonData.Fingerprint(replacement.HeaderPolicies);
}

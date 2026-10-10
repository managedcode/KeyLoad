namespace KeyLoad.Core.Features.Authorization;

/// <summary>Validates the compare-and-set boundary for resource policy updates.</summary>
internal static class ResourcePolicyUpdates
{
    private const string MissingResourceMessage = "The resource does not exist at the expected schema version.";
    private const string StaleVersionMessage = "The expected resource schema version is stale.";
    private const string InvalidVersionMessage = "A policy update must increment the resource schema version exactly once.";
    private const string NoPolicyChangeMessage = "A policy update must change at least one supported policy.";
    private const string UnsupportedDefinitionChangeMessage =
        "The requested resource definition change is unsupported.";
    private const string VersionExhaustedMessage = "The resource schema version cannot be incremented.";

    /// <summary>Validates a resource creation, identical current definition, or policy-only CAS.</summary>
    /// <param name="previous">The definition read from the current apply transaction, if any.</param>
    /// <param name="replacement">The complete replacement definition.</param>
    /// <param name="expectedVersion">The expected current version, or null for current create or
    /// identical-definition semantics.</param>
    internal static void Validate(ResourceDefinition? previous, ResourceDefinition replacement, long? expectedVersion)
    {
        const int SchemaVersionStep = 1;

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
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDefinitionChangeMessage);
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
        if (replacement.SchemaVersion != previous.SchemaVersion + SchemaVersionStep)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVersionMessage);
        }
        if (!SameNonPolicyDefinition(previous, replacement))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDefinitionChangeMessage);
        }
        if (SamePolicies(previous, replacement))
        {
            throw Errors.Fail(ErrorCode.Validation, NoPolicyChangeMessage);
        }
    }

    internal static bool SameNonPolicyDefinition(ResourceDefinition previous, ResourceDefinition replacement)
    {
        const int SchemaVersionEmptyCount = 0;

        var previousShape = previous with { FieldPolicies = [], HeaderPolicies = [], SchemaVersion = SchemaVersionEmptyCount };
        var replacementShape = replacement with { FieldPolicies = [], HeaderPolicies = [], SchemaVersion = SchemaVersionEmptyCount };
        if (previous.Kind == ResourceKind.WorkQueue)
        {
            replacementShape = replacementShape with
            {
                QueuePolicy = replacementShape.QueuePolicy with
                {
                    OrderingProfile = previous.QueuePolicy.OrderingProfile,
                    ParkedHeadPolicy = previous.QueuePolicy.ParkedHeadPolicy,
                    RetryJitter = previous.QueuePolicy.RetryJitter,
                    RetryExponentialFactor = previous.QueuePolicy.RetryExponentialFactor
                }
            };
        }
        return JsonData.Fingerprint(previousShape) == JsonData.Fingerprint(replacementShape);
    }

    private static bool SamePolicies(ResourceDefinition previous, ResourceDefinition replacement)
        => JsonData.Fingerprint(previous.FieldPolicies) == JsonData.Fingerprint(replacement.FieldPolicies)
            && JsonData.Fingerprint(previous.HeaderPolicies) == JsonData.Fingerprint(replacement.HeaderPolicies)
            && JsonData.Fingerprint(previous.QueuePolicy) == JsonData.Fingerprint(replacement.QueuePolicy);
}

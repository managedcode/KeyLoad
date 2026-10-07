using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.Search;

/// <summary>Validates immutable field profiles and compares declarations against persisted authority.</summary>
internal static class VectorFieldProfiles
{
    private const int MaximumProfiles = 32;
    private const int MinimumDimension = 1;
    private const int MaximumDimension = 4_096;
    private const string Invalid = "The configured vector field profile is invalid.";
    private const string OverBudget = "The configured vector field profiles exceed their budget.";
    private const string Mismatch = "The declared vector profile does not match the configured field.";
    internal static void Validate(ResourceDefinition definition, ErrorCode invalidCode = ErrorCode.Validation)
    {
        if (definition.VectorProfiles.IsDefault || !definition.VectorProfiles.IsEmpty && definition.Kind != ResourceKind.Collection)
        { throw Errors.Fail(invalidCode, Invalid); }
        if (definition.VectorProfiles.Length > MaximumProfiles)
        { throw Errors.Fail(invalidCode == ErrorCode.Corruption ? invalidCode : ErrorCode.ResourceExhausted, OverBudget); }
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in definition.VectorProfiles)
        {
            if (profile is null || profile.Space is null || string.IsNullOrEmpty(profile.Field)
                || profile.Space.Dimension is < MinimumDimension or > MaximumDimension || !Enum.IsDefined(profile.Space.Metric)
                || !fields.Add(profile.Field))
            { throw Errors.Fail(invalidCode, Invalid); }
            JsonData.Identifier(profile.Space.Id);
            JsonData.Identifier(profile.Space.Model);
            JsonData.Identifier(profile.Space.Version);
            var parts = JsonData.PathSegments(profile.Field);
            var canonical = JsonPointerPaths.Encode(parts);
            if (profile.Field != canonical)
            { throw Errors.Fail(invalidCode, Invalid); }
        }
    }

    internal static void Require(ResourceDefinition definition, PutVector vector)
    {
        try
        { Validate(definition, ErrorCode.Corruption); }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.Validation)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var configured = definition.VectorProfiles.FirstOrDefault(profile => profile.Field == vector.Field);
        if (configured is not null && configured.Space != vector.Space)
        { throw Errors.Fail(ErrorCode.Validation, Mismatch); }
    }
}

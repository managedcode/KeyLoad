using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Separates complete selected source redaction from publicly visible policy paths.</summary>
internal static class QueryProjectionRedactions
{
    internal static (bool Redacted, ImmutableArray<string> Fields) Selected(DatabaseEngine database,
        ResourceDefinition resource, DocumentResult safe, ImmutableArray<string> completeOmittedPaths,
        ImmutableArray<Selection> selections, string? sourceAlias = null, ResourceDefinition? additionalResource = null)
    {
        if (completeOmittedPaths.IsEmpty)
        {
            return (false, []);
        }
        var relevant = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            if (sourceAlias is not null && !StringComparer.Ordinal.Equals(selection.SourceAlias, sourceAlias))
            {
                continue;
            }
            if (selection.Path == SqlSyntax.Star)
            {
                return (true, safe.RedactedFields);
            }
            if (selection.Path is SqlSyntax.MetadataIdPath or SqlSyntax.MetadataRevisionPath)
            {
                continue;
            }
            var policies = database.Authorization.GetEffectiveFieldPolicies(resource, selection.Path);
            if (additionalResource is not null)
            {
                policies = policies.Concat(database.Authorization.GetEffectiveFieldPolicies(additionalResource, selection.Path));
            }
            foreach (var policy in policies)
            {
                relevant.Add(policy.Path);
            }
        }
        var redacted = completeOmittedPaths.Any(relevant.Contains);
        var visible = safe.RedactedFields.Where(relevant.Contains).ToImmutableArray();
        return (redacted, visible);
    }
}

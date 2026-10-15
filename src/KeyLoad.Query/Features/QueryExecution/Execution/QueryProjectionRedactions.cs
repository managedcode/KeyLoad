using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Retains only original source policy paths contributing to the selected row.</summary>
internal static class QueryProjectionRedactions
{
    internal static ImmutableArray<string> Selected(DatabaseEngine database, ResourceDefinition resource,
        DocumentResult safe, ImmutableArray<Selection> selections, string? sourceAlias = null)
    {
        if (safe.RedactedFields.IsEmpty)
        {
            return [];
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
                return safe.RedactedFields;
            }
            foreach (var policy in database.Authorization.GetEffectiveFieldPolicies(resource, selection.Path))
            {
                relevant.Add(policy.Path);
            }
        }
        return safe.RedactedFields.Where(relevant.Contains).ToImmutableArray();
    }
}

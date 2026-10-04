using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Maps source payload and header field grants onto the model-row schema.</summary>
internal static class ModelQueryPolicies
{
    internal static ResourceDefinition Rebase(ResourceDefinition resource)
    {
        var fields = resource.FieldPolicies.Select(policy => policy with { Path = Rebase("payload", policy.Path) })
            .Concat(resource.HeaderPolicies.Select(policy => policy with { Path = Rebase("headers", policy.Path) }))
            .ToImmutableArray();
        return resource with { FieldPolicies = fields, HeaderPolicies = [] };
    }

    private static string Rebase(string root, string path)
        => path.StartsWith('/') ? "/" + root + path : "/" + root + "/" + path;
}

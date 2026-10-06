using System.Collections.Immutable;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Maps source payload and header field grants onto the model-row schema.</summary>
internal static class ModelQueryPolicies
{
    private const string PayloadModelField = "payload";
    private const string HeadersModelField = "headers";
    private const char PathSeparator = '/';
    private const string RootFieldPath = "/";

    internal static ResourceDefinition Rebase(ResourceDefinition resource)
    {
        var fields = resource.FieldPolicies.Select(policy => policy with { Path = Rebase(PayloadModelField, policy.Path) })
            .Concat(resource.HeaderPolicies.Select(policy => policy with { Path = Rebase(HeadersModelField, policy.Path) }))
            .ToImmutableArray();
        return resource with { FieldPolicies = fields, HeaderPolicies = [] };
    }

    private static string Rebase(string root, string path)
        => path.StartsWith(PathSeparator) ? RootFieldPath + root + path : RootFieldPath + root + RootFieldPath + path;
}

using System.Text.Json.Nodes;
using KeyLoad.Core;

namespace KeyLoad.Security;

public sealed class AuthorizationPolicy : IAuthorizationPolicy
{
    private static bool Grant(PrincipalRecord principal, string grant) => principal.ClusterAdministrator
        || principal.FieldGrants.Contains(grant, StringComparer.Ordinal) || principal.FieldGrants.Contains("*", StringComparer.Ordinal);
    public void Require(PrincipalRecord principal, PartitionRef partition, string resource, Capability capability)
    {
        if (principal.ClusterAdministrator) return;
        if (principal.TenantId != partition.TenantId || !principal.Grants.Any(g => (g.Database == "*" || g.Database == partition.DatabaseId)
            && (g.Resource == "*" || g.Resource == resource) && (g.Capabilities & capability) == capability))
            throw Errors.Fail(ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.");
    }
    public bool CanReadRow(PrincipalRecord principal, RowAccess access) => principal.ClusterAdministrator || !principal.RestrictRows
        || access.OwnerId is not null && access.OwnerId == principal.OwnerId
        || access.ProjectId is not null && principal.Projects.Contains(access.ProjectId, StringComparer.Ordinal);
    public void RequireWriteRow(PrincipalRecord principal, RowAccess access)
    {
        if (!CanReadRow(principal, access)) throw Errors.Fail(ErrorCode.PermissionDenied, "The principal cannot write this row scope.");
    }
    public void RequireFieldUse(PrincipalRecord principal, ResourceDefinition resource, string path)
    {
        foreach (var policy in resource.FieldPolicies)
            if (Overlaps(policy.Path, path) && !Grant(principal, policy.RawUseGrant))
                throw Errors.Fail(ErrorCode.PermissionDenied, "The query requires a protected field-use grant.");
    }
    public void RequireFieldWrite(PrincipalRecord principal, ResourceDefinition resource, string path)
    {
        foreach (var policy in resource.FieldPolicies)
            if (Overlaps(policy.Path, path) && !Grant(principal, policy.WriteGrant))
                throw Errors.Fail(ErrorCode.PermissionDenied, "The mutation requires a protected field-write grant.");
    }
    public void RequireReplacement(PrincipalRecord principal, ResourceDefinition resource, bool explicitReplacement)
    {
        if (resource.FieldPolicies.Any(p => !Grant(principal, p.RawReadGrant))
            && (!explicitReplacement || !Grant(principal, "document.replaceSensitive")))
            throw Errors.Fail(ErrorCode.PermissionDenied, "Replacing a redacted document requires an explicit replacement grant. Use a field patch.");
    }
    public void RequireWorkerInput(PrincipalRecord principal, ResourceDefinition resource)
    {
        if (resource.FieldPolicies.Concat(resource.HeaderPolicies).Any(p => p.RequiredForProcessing && !Grant(principal, p.RawReadGrant)))
            throw Errors.Fail(ErrorCode.PermissionDenied, "Required worker input is protected by a missing raw-read grant.");
    }
    public string Project(PrincipalRecord principal, SensitiveFieldPolicy[] policies, string json, out string[] omitted)
    {
        var hidden = policies.Where(p => !Grant(principal, p.RawReadGrant)).ToArray();
        omitted = hidden.Select(p => p.Path).ToArray();
        if (hidden.Length == 0) return json;
        if (hidden.Any(p => p.Path.Length == 0)) return "{}";
        var root = JsonNode.Parse(json);
        foreach (var policy in hidden) Remove(root, JsonData.PathSegments(policy.Path), 0);
        return root?.ToJsonString() ?? "null";
    }
    private static void Remove(JsonNode? node, string[] path, int position)
    {
        if (node is null || position >= path.Length) return;
        if (node is JsonObject obj)
        {
            foreach (var key in (path[position] == "*" ? obj.Select(p => p.Key).ToArray() : [path[position]]))
            {
                if (position == path.Length - 1) obj.Remove(key);
                else Remove(obj[key], path, position + 1);
            }
        }
        else if (node is JsonArray array)
        {
            if (path[position] == "*")
                for (var i = 0; i < array.Count; i++)
                {
                    if (position == path.Length - 1) array[i] = null;
                    else Remove(array[i], path, position + 1);
                }
            else if (int.TryParse(path[position], out var index) && index >= 0 && index < array.Count)
            {
                if (position == path.Length - 1) array[index] = null;
                else Remove(array[index], path, position + 1);
            }
        }
    }
    public static bool Overlaps(string protectedPath, string path)
    {
        var left = JsonData.PathSegments(protectedPath);
        var right = JsonData.PathSegments(path);
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            if (left[i] != "*" && right[i] != "*" && left[i] != right[i]) return false;
        return true;
    }
}

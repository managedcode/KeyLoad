using System.Text.Json;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string CachedDeliveryJsonInvalidDetail = "The original delivery JSON is invalid.";
    private const string CachedDeliveryPolicyChangedDetail = "The current resource policy protects data retained in the original delivery.";

    private void RequireCachedDeliveryProjection(PrincipalRecord principal, ResourceDefinition resource,
        string payload, string headers)
    {
        try
        {
            var projectedPayload = Authorization.Project(principal, resource.FieldPolicies, payload, out _);
            var projectedHeaders = Authorization.Project(principal, resource.HeaderPolicies, headers, out _);
            if (!CachedProjectionMatches(payload, projectedPayload) || !CachedProjectionMatches(headers, projectedHeaders))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, CachedDeliveryPolicyChangedDetail);
            }
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, CachedDeliveryJsonInvalidDetail);
        }
    }

    private bool CachedProjectionMatches(string original, string projected)
    {
        if (string.Equals(original, projected, StringComparison.Ordinal))
        {
            return true;
        }
        var options = new JsonDocumentOptions { MaxDepth = Limits.MaxJsonDepth };
        using var originalDocument = JsonDocument.Parse(original, options);
        using var projectedDocument = JsonDocument.Parse(projected, options);
        return JsonElement.DeepEquals(originalDocument.RootElement, projectedDocument.RootElement);
    }
}

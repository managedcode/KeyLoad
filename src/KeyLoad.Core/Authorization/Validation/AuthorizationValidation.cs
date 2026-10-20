using System.Text.Json;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizationValidation operations against the caller's database context.</summary>
public static class AuthorizationValidation
{
    internal const string CachedDeliveryJsonInvalidDetail = "The original delivery JSON is invalid.";
    internal const string CachedDeliveryPolicyChangedDetail = "The current resource policy protects data retained in the original delivery.";
    internal static void RequireCachedDeliveryProjection(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, string payload, string headers)
    {
        try
        {
            var projectedPayload = engine.Authorization.Project(principal, resource.FieldPolicies, payload, out _);
            var projectedHeaders = engine.Authorization.Project(principal, resource.HeaderPolicies, headers, out _);
            if (!engine.CachedProjectionMatches(payload, projectedPayload) || !engine.CachedProjectionMatches(headers, projectedHeaders))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, CachedDeliveryPolicyChangedDetail);
            }
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, CachedDeliveryJsonInvalidDetail);
        }
    }

    internal static bool CachedProjectionMatches(this DatabaseEngine engine, string original, string projected)
    {
        if (string.Equals(original, projected, StringComparison.Ordinal))
        {
            return true;
        }

        var options = new JsonDocumentOptions
        {
            MaxDepth = engine.Limits.MaxJsonDepth
        };
        using var originalDocument = JsonDocument.Parse(original, options);
        using var projectedDocument = JsonDocument.Parse(projected, options);
        return JsonElement.DeepEquals(originalDocument.RootElement, projectedDocument.RootElement);
    }
}

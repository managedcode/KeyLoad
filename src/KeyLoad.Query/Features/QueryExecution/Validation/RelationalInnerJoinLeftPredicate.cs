using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using System.Text.Json;

namespace KeyLoad.Query;

/// <summary>Validates and evaluates left-side predicates within the original authorized join view.</summary>
internal static class RelationalInnerJoinLeftPredicate
{
    private const int SingleFieldSegment = 1;
    private const int FirstSegment = 0;
    private const string UnsupportedShapeDetail = "The query AST version is unsupported.";
    private const string UnsupportedDetail = "The relational join predicate must use declared top-level left fields.";

    private static bool IsSupported(Predicate predicate)
        => PredicateEvaluator.Fields(predicate).All(field =>
            JsonData.PathSegments(field).Length == SingleFieldSegment
            && field is not SqlSyntax.MetadataIdPath and not SqlSyntax.MetadataRevisionPath);

    internal static void ValidateParameters(Predicate? predicate, Dictionary<string, JsonElement>? parameters)
    {
        if (predicate is not null && !IsSupported(predicate))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedShapeDetail);
        }
        PredicateEvaluator.CheckParameters(predicate, parameters);
    }

    internal static void Authorize(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition leftResource, AstQueryRequest request,
        Dictionary<string, RelationalColumn> leftColumns)
    {
        foreach (var field in PredicateEvaluator.Fields(request.Query.Filter))
        {
            var segments = JsonData.PathSegments(field);
            if (segments.Length != SingleFieldSegment || !leftColumns.ContainsKey(segments[FirstSegment]))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDetail);
            }
        }
        QueryFieldAuthorization.Validate(database, principal, leftResource, request);
    }

    internal static bool Matches(AstQueryRequest request, DocumentRecord left, JsonElement json)
        => request.Query.Filter is null
           || PredicateEvaluator.Evaluate(request.Query.Filter, left, json, request.Parameters) == true;
}

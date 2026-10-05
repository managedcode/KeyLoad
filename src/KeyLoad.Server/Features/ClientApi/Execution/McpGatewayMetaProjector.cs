using System.Collections.Immutable;
using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Projects native graph results to current canonical KeyLoad schemas and hints.</summary>
internal static class McpGatewayMetaProjector
{
    private const string InvalidResult = "The native tool catalog returned an invalid result.";

    internal static McpGatewaySearchProjection Search(McpGatewaySearchResult result, int maximumMatches)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Matches is null || result.Matches.Count > maximumMatches)
        { throw InvalidNativeResult(); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = ImmutableArray.CreateBuilder<McpGatewayScoredTool>(maximumMatches);
        foreach (var match in result.Matches)
        {
            var tool = CanonicalTool(match.ToolName);
            ValidateScore(match.Score);
            if (seen.Add(tool.Name))
            { matches.Add(new(tool, match.Score)); }
        }
        return new(matches.ToImmutable());
    }

    internal static McpGatewayRouteProjection Route(McpGatewayToolRouteResult result,
        int maximumCategories, int maximumPerCategory)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Categories is null || result.Categories.Count > maximumCategories)
        { throw InvalidNativeResult(); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var categories = ImmutableArray.CreateBuilder<McpGatewayRouteCategoryProjection>(maximumCategories);
        foreach (var category in result.Categories)
        {
            ValidateCategory(category, maximumPerCategory);
            var tools = ImmutableArray.CreateBuilder<McpGatewayScoredTool>(maximumPerCategory);
            foreach (var match in category.Tools)
            {
                var tool = CanonicalTool(match.ToolName);
                ValidateScore(match.Score);
                if (seen.Add(tool.Name))
                { tools.Add(new(tool, match.Score)); }
            }
            if (tools.Count > 0)
            { categories.Add(new(category.Category, category.Score, tools.ToImmutable())); }
        }
        if (seen.Count > checked(maximumCategories * maximumPerCategory))
        { throw InvalidNativeResult(); }
        return new(categories.ToImmutable());
    }

    private static Tool CanonicalTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !McpOperationCatalog.TryGetTool(name, out var descriptor)
            || McpGatewayMetaProtocol.TryGetOperation(name, out _))
        { throw InvalidNativeResult(); }
        return descriptor!.CreateTool();
    }

    private static void ValidateCategory(McpGatewayToolRouteCategory category, int maximumPerCategory)
    {
        if (category is null || string.IsNullOrWhiteSpace(category.Category)
            || !double.IsFinite(category.Score)
            || category.Tools is null || category.Tools.Count > maximumPerCategory)
        { throw InvalidNativeResult(); }
    }

    private static void ValidateScore(double score)
    {
        if (!double.IsFinite(score))
        { throw InvalidNativeResult(); }
    }

    private static KeyLoadException InvalidNativeResult()
        => Errors.Fail(ErrorCode.RecoveryRequired, InvalidResult);
}

using System.Collections.Immutable;
using ManagedCode.MCPGateway;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Projects execution graph results to current canonical KeyLoad schemas and hints.</summary>
internal static class McpGatewayMetaProjector
{
    private const string InvalidResult = "The execution tool catalog returned an invalid result.";

    internal static McpGatewaySearchProjection Search(McpGatewaySearchResult result, int maximumMatches)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Matches is null || result.Matches.Count > maximumMatches)
        {
            throw RuntimeInvalidResult();
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = ImmutableArray.CreateBuilder<McpGatewayScoredTool>(maximumMatches);
        foreach (var match in result.Matches)
        {
            var tool = CanonicalTool(match.ToolName);
            ValidateScore(match.Score);
            if (seen.Add(tool.Name))
            {
                matches.Add(new(tool, match.Score));
            }
        }
        return new(matches.ToImmutable());
    }

    internal static McpGatewayRouteProjection Route(McpGatewayToolRouteResult result,
        int maximumCategories, int maximumPerCategory)
    {
        const int ToolsCountValidationBoundary = 0;

        ArgumentNullException.ThrowIfNull(result);
        if (result.Categories is null || result.Categories.Count > maximumCategories)
        {
            throw RuntimeInvalidResult();
        }
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
                {
                    tools.Add(new(tool, match.Score));
                }
            }
            if (tools.Count > ToolsCountValidationBoundary)
            {
                categories.Add(new(category.Category, category.Score, tools.ToImmutable()));
            }
        }
        if (seen.Count > checked(maximumCategories * maximumPerCategory))
        {
            throw RuntimeInvalidResult();
        }
        return new(categories.ToImmutable());
    }

    private static Tool CanonicalTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !McpOperationCatalog.TryGetTool(name, out var descriptor)
            || McpGatewayMetaProtocol.TryGetOperation(name, out _))
        {
            throw RuntimeInvalidResult();
        }
        return descriptor!.CreateTool();
    }

    private static void ValidateCategory(McpGatewayToolRouteCategory category, int maximumPerCategory)
    {
        if (category is null || string.IsNullOrWhiteSpace(category.Category)
            || !double.IsFinite(category.Score)
            || category.Tools is null || category.Tools.Count > maximumPerCategory)
        {
            throw RuntimeInvalidResult();
        }
    }

    private static void ValidateScore(double score)
    {
        if (!double.IsFinite(score))
        {
            throw RuntimeInvalidResult();
        }
    }

    private static KeyLoadException RuntimeInvalidResult()
        => Errors.Fail(ErrorCode.RecoveryRequired, InvalidResult);
}

using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Enforces the bounded native search/route surface even when called outside protocol validation.</summary>
internal static class McpGatewayCatalogRequestValidation
{
    private const int MinimumResultCount = 1;
    private const string InvalidQuery = "The MCP discovery query is invalid.";
    private const string InvalidLimit = "The MCP discovery limit is invalid.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidateQuery(string query, IOptions<McpExecutionOptions> executionOptions)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
        }

        try
        {
            if (StrictUtf8.GetByteCount(query) > executionOptions.Value.MaximumDiscoveryQueryBytes)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
            }
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
        }
    }

    internal static void ValidateSearchLimit(int limit, IOptions<McpExecutionOptions> executionOptions)
    {
        if (limit < MinimumResultCount || limit > executionOptions.Value.MaximumSearchResults)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimit);
        }
    }

    internal static void ValidateRouteLimits(int categories, int toolsPerCategory, IOptions<McpExecutionOptions> executionOptions)
    {
        if (categories < MinimumResultCount || categories > executionOptions.Value.MaximumRouteResults
            || toolsPerCategory < MinimumResultCount || toolsPerCategory > executionOptions.Value.MaximumRouteResults)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimit);
        }
    }
}

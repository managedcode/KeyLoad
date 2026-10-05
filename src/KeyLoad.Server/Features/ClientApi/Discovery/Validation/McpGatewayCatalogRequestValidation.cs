using System.Text;

namespace KeyLoad.Server;

/// <summary>Enforces the bounded native search/route surface even when called outside protocol validation.</summary>
internal static class McpGatewayCatalogRequestValidation
{
    internal const int MaximumQueryUtf8Bytes = 2048;
    internal const int MaximumRouteLimit = 2;
    private const string InvalidQuery = "The MCP discovery query is invalid.";
    private const string InvalidLimit = "The MCP discovery limit is invalid.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidateQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
        }

        try
        {
            if (StrictUtf8.GetByteCount(query) > MaximumQueryUtf8Bytes)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
            }
        }
        catch (EncoderFallbackException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQuery);
        }
    }

    internal static void ValidateSearchLimit(int limit)
    {
        if (limit is < 1 or > McpGatewayCatalogValidation.NativeMaximumResults)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimit);
        }
    }

    internal static void ValidateRouteLimits(int categories, int toolsPerCategory)
    {
        if (categories is < 1 or > MaximumRouteLimit || toolsPerCategory is < 1 or > MaximumRouteLimit)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLimit);
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Search;

internal static class TextPublicOutcomeAssertions
{
    private const int FailureReasonUtf8ByteLimit = 128;
    private const string UnclassifiedMcpError = "MCP error Unclassified.";

    internal static async Task<T> SdkSuccessAsync<T>(Result<T> result)
    {
        await Assert.That(result.IsSuccess).IsTrue().Because("The canonical SDK outcome reported "
            + ClosedErrorCode(result.Problem?.ErrorCode) + ". " + (result.Problem?.Detail ?? string.Empty));
        return await McpCallerAssertions.SdkSuccessAsync(result);
    }

    internal static async Task<McpCallReceipt<T>> SuccessAsync<T>(CallToolResult reply)
    {
        await Assert.That(reply.IsError is true).IsFalse().Because(PublicExplanation(reply));
        return await McpCallerAssertions.SuccessAsync<T>(reply);
    }

    private static string ClosedErrorCode(string? actual)
        => actual is { Length: > 0 and <= 64 } && Enum.TryParse<ErrorCode>(actual, out var parsed)
            && Enum.IsDefined(parsed) && string.Equals(parsed.ToString(), actual, StringComparison.Ordinal)
            ? parsed.ToString() : "Unclassified";

    private static string PublicExplanation(CallToolResult reply)
    {
        if (reply.StructuredContent is not { } envelope || envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty(McpCallerProtocol.Error, out var problem)
            || problem.ValueKind != JsonValueKind.Object)
        {
            return UnclassifiedMcpError;
        }
        var code = problem.TryGetProperty(McpCallerProtocol.ProblemCode, out var errorCode)
            && errorCode.ValueKind == JsonValueKind.String ? ClosedErrorCode(errorCode.GetString()) : "Unclassified";
        var reason = UnclassifiedMcpError;
        if (code != "Unclassified" && Enum.TryParse<ErrorCode>(code, out var parsed))
        {
            var classified = string.Concat("MCP safe error code ", code, " status ",
                Errors.Status(parsed).ToString(CultureInfo.InvariantCulture), ".");
            if (Encoding.UTF8.GetByteCount(classified) < FailureReasonUtf8ByteLimit)
            {
                reason = classified;
            }
        }
        var detail = problem.TryGetProperty(McpCallerProtocol.ProblemDetail, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        return string.Concat(reason, " ", detail);
    }
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Describes only the outcome that the official MCP client actually exposed.</summary>
internal sealed record RequestCqrsFaultMcpObservation(CallToolResult? ToolResult, Exception? TransportFailure);

/// <summary>Captures caller-visible interruption without manufacturing a protocol response.</summary>
internal static class RequestCqrsFaultOutcome
{
    internal const string MissingBaseline = "The public baseline document was unavailable.";
    internal const string EmptyCommandId = "The stable command identifier must not be empty.";
    internal const string McpRetryTransportFailure = "The official MCP retry did not return a validated tool result.";

    /// <summary>Requires the SDK's real write result to classify an unavailable receipt as unknown.</summary>
    /// <param name="result">The actual result returned by KeyLoadClient.</param>
    /// <returns>The completed assertions.</returns>
    internal static async Task AssertSdkUnknownWriteAsync(Result<CommitReceipt> result)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
    }

    /// <summary>Accepts an actual MCP safe tool error or an actual transport/cancellation exception.</summary>
    /// <param name="observation">The exact official-client observation.</param>
    /// <returns>The completed assertions.</returns>
    internal static async Task<Guid?> AssertMcpInterruptionAsync(RequestCqrsFaultMcpObservation observation)
    {
        var hasResult = observation.ToolResult is not null;
        var hasFailure = observation.TransportFailure is not null;
        await Assert.That(hasResult ^ hasFailure).IsTrue();
        if (observation.ToolResult is { } result)
        {
            return await McpCallerAssertions.ErrorAsync(result, ErrorCode.UnknownWriteOutcome, dispatched: true);
        }

        await Assert.That(observation.TransportFailure is OperationCanceledException
            or HttpRequestException or IOException).IsTrue();
        return null;
    }
}

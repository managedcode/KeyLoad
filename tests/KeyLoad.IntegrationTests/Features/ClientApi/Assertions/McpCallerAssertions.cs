using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Verifies observable native SDK envelopes without replacing server behavior or exposing private failures.</summary>
internal static class McpCallerAssertions
{
    private const int FailureReasonUtf8ByteLimit = 128;
    private const string UnclassifiedMcpError = "MCP error Unclassified.";
    internal static ImmutableArray<string> ProblemFields { get; } =
    [McpCallerProtocol.ProblemType, McpCallerProtocol.ProblemTitle, McpCallerProtocol.ProblemStatus,
        McpCallerProtocol.ProblemDetail, McpCallerProtocol.ProblemCode];

    /// <summary>Requires a genuine successful canonical HTTP SDK outcome without logging its private error detail.</summary>
    /// <typeparam name="T">The public result type.</typeparam>
    /// <param name="result">The actual HTTP SDK result.</param>
    /// <returns>The actual typed value, including an allowed null result.</returns>
    internal static async Task<T> SdkSuccessAsync<T>(Result<T> result)
    {
        await Assert.That(result.IsSuccess).IsTrue().Because("The canonical SDK outcome reported "
            + ClosedErrorCode(result.Problem?.ErrorCode) + ".");
        return result.Value!;
    }

    private static string ClosedErrorCode(string? actual)
        => actual is { Length: > 0 and <= 64 } && Enum.TryParse<ErrorCode>(actual, out var parsed)
            && Enum.IsDefined(parsed) && string.Equals(parsed.ToString(), actual, StringComparison.Ordinal)
            ? parsed.ToString() : "Unclassified";

    private static string SafeMcpFailureReason(CallToolResult reply)
    {
        if (reply.StructuredContent is not { } envelope || envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty(McpCallerProtocol.Error, out var problem)
            || problem.ValueKind != JsonValueKind.Object
            || !problem.TryGetProperty(McpCallerProtocol.ProblemCode, out var errorCode)
            || errorCode.ValueKind != JsonValueKind.String)
        { return UnclassifiedMcpError; }

        var code = ClosedErrorCode(errorCode.GetString());
        if (code == "Unclassified" || !Enum.TryParse<ErrorCode>(code, out var parsed))
        { return UnclassifiedMcpError; }
        var reason = string.Concat("MCP safe error code ", code, " status ",
            Errors.Status(parsed).ToString(CultureInfo.InvariantCulture), ".");
        return Encoding.UTF8.GetByteCount(reason) < FailureReasonUtf8ByteLimit ? reason : UnclassifiedMcpError;
    }

    /// <summary>Requires the exact success wrapper, a nonempty execution GUID and a bounded native text summary.</summary>
    /// <typeparam name="T">The public canonical result type.</typeparam>
    /// <param name="reply">The actual official SDK response.</param>
    /// <returns>The canonical value and the operation's actual execution identifier.</returns>
    internal static async Task<McpCallReceipt<T>> SuccessAsync<T>(CallToolResult reply)
    {
        await Assert.That(reply.IsError is true).IsFalse().Because(SafeMcpFailureReason(reply));
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        await VerifyKeysAsync(envelope, [McpCallerProtocol.Result, McpCallerProtocol.RequestId]);
        var id = envelope.GetProperty(McpCallerProtocol.RequestId).GetGuid();
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        await VerifySummaryAsync(reply);
        return new(envelope.GetProperty(McpCallerProtocol.Result).Deserialize<T>(JsonDefaults.Options)!, id);
    }

    /// <summary>Requires a safe domain failure and its correct before-dispatch or after-dispatch identity.</summary>
    /// <param name="reply">The native error result.</param>
    /// <param name="expected">The required domain error.</param>
    /// <param name="dispatched">Whether a real operation grain must have started.</param>
    /// <returns>The actual operation GUID, or null before dispatch.</returns>
    internal static async Task<Guid?> ErrorAsync(CallToolResult reply, ErrorCode expected, bool dispatched)
    {
        await Assert.That(reply.IsError is true).IsTrue();
        var envelope = reply.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult);
        await VerifyKeysAsync(envelope, [McpCallerProtocol.Error, McpCallerProtocol.RequestId]);
        await VerifyProblemAsync(envelope.GetProperty(McpCallerProtocol.Error), expected);
        var requestId = envelope.GetProperty(McpCallerProtocol.RequestId);
        await Assert.That(requestId.ValueKind == JsonValueKind.Null).IsEqualTo(!dispatched);
        await VerifySummaryAsync(reply);
        if (!dispatched)
        { return null; }
        var id = requestId.GetGuid();
        await Assert.That(id).IsNotEqualTo(Guid.Empty);
        return id;
    }

    /// <summary>Requires the exact canonical five-field safe Problem with the expected code and HTTP category.</summary>
    /// <param name="problem">The actual returned Problem JSON.</param>
    /// <param name="expected">The required safe error classification.</param>
    /// <returns>The completed assertions.</returns>
    internal static async Task VerifyProblemAsync(JsonElement problem, ErrorCode expected)
    {
        await VerifyKeysAsync(problem, ProblemFields);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemCode).GetString()).IsEqualTo(expected.ToString());
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32()).IsEqualTo(Errors.Status(expected));
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemType).ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemTitle).ValueKind).IsEqualTo(JsonValueKind.String);
        await Assert.That(problem.GetProperty(McpCallerProtocol.ProblemDetail).ValueKind).IsEqualTo(JsonValueKind.String);
    }

    /// <summary>Checks private credential and payload omission using boolean assertions which never print those values.</summary>
    /// <param name="reply">The actual returned native reply.</param>
    /// <param name="credential">The private test credential that must not be returned.</param>
    /// <param name="privateValue">The private payload marker that must not be returned.</param>
    /// <returns>The completed omission assertions.</returns>
    internal static async Task DoesNotDiscloseAsync(CallToolResult reply, string credential, string privateValue)
    {
        var actual = JsonSerializer.Serialize(reply);
        await Assert.That(actual.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(actual.Contains(privateValue, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task VerifyKeysAsync(JsonElement value, ImmutableArray<string> expected)
    {
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Object);
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(names.Length).IsEqualTo(expected.Length);
        await Assert.That(names.ToHashSet(StringComparer.Ordinal).SetEquals(expected)).IsTrue();
    }

    private static async Task VerifySummaryAsync(CallToolResult reply)
    {
        await Assert.That(reply.Content.Count).IsGreaterThan(0);
        await Assert.That(reply.Content.All(content => content is TextContentBlock)).IsTrue();
        var bytes = reply.Content.Cast<TextContentBlock>().Sum(content => Encoding.UTF8.GetByteCount(content.Text));
        await Assert.That(bytes).IsGreaterThan(0);
        await Assert.That(bytes).IsLessThanOrEqualTo(McpCallerProtocol.SummaryByteLimit);
    }
}

using System.Text;
using System.Text.Json;
using KeyLoad.Server;
using ModelContextProtocol.Protocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/005/007: wrappers preserve canonical values and borrow one explicitly owned document.</summary>
internal sealed class McpReplyOwnerTests
{
    /// <summary>Large integers, number spellings, Unicode and escaped strings retain their exact canonical semantics.</summary>
    [Test]
    public async Task SuccessPreservesCanonicalResultAndExecutionIdentity()
    {
        var canonical = Encoding.UTF8.GetBytes(McpOutputTestData.CanonicalJson);
        using var owner = McpReplyOwner.Success(canonical, McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        var tool = owner.ToolResult();
        var root = tool.StructuredContent!.Value;
        using var original = JsonDocument.Parse(canonical);
        await Assert.That(JsonElement.DeepEquals(root.GetProperty(McpOutputTestData.ResultField), original.RootElement)).IsTrue();
        await Assert.That(root.GetProperty(McpOutputTestData.ResultField).GetRawText()).IsEqualTo(McpOutputTestData.CanonicalJson);
        await Assert.That(root.GetProperty(McpOutputTestData.RequestIdField).GetGuid()).IsEqualTo(McpOutputTestData.ExecutionId);
        await Assert.That(tool.IsError).IsFalse();
        await Assert.That(tool.Content.Count).IsEqualTo(1);
        await Assert.That(((TextContentBlock)tool.Content[0]).Text.Length).IsLessThanOrEqualTo(McpOutputTestData.MaximumSummaryCharacters);
        await Assert.That(((TextContentBlock)tool.Content[0]).Text).DoesNotContain(McpOutputTestData.Marker);
    }

    /// <summary>Every defined code creates fresh safe problem data without accepting exception text or payload messages.</summary>
    [Test]
    public async Task FailurePreservesCodeStatusAndNullableRequestIdentity()
    {
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            using var owner = McpReplyOwner.Failure(code, null, McpOutputTestData.MaximumBytes);
            var tool = owner.ToolResult();
            var root = tool.StructuredContent!.Value;
            var problem = root.GetProperty(McpOutputTestData.ErrorField);
            var detail = problem.GetProperty(McpOutputTestData.DetailField).GetString();
            using var expected = JsonDocument.Parse(JsonDefaults.Serialize(Errors.Problem(code, detail!)));
            await Assert.That(JsonElement.DeepEquals(problem, expected.RootElement)).IsTrue();
            await Assert.That(detail).IsNotNull();
            await Assert.That(detail!.Length).IsGreaterThan(0);
            await Assert.That(Encoding.UTF8.GetString(owner.Bytes.Span)).DoesNotContain(McpOutputTestData.Marker);
            await Assert.That(root.GetProperty(McpOutputTestData.RequestIdField).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(tool.IsError).IsTrue();
            await Assert.That(tool.Content.Count).IsEqualTo(1);
            await Assert.That(((TextContentBlock)tool.Content[0]).Text.Length).IsLessThanOrEqualTo(McpOutputTestData.MaximumSummaryCharacters);
        }
    }

    /// <summary>Each native result and content collection is fresh while structured content borrows the same owned document.</summary>
    [Test]
    public async Task ToolResultsAreFreshAndOwnerDisposalInvalidatesBorrowedDocument()
    {
        using var owner = McpReplyOwner.Failure(ErrorCode.UnknownWriteOutcome, McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes);
        var first = owner.ToolResult();
        var second = owner.ToolResult();
        var borrowed = owner.Bytes;
        var element = first.StructuredContent!.Value;
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(ReferenceEquals(first.Content, second.Content)).IsFalse();
        first.Content.Clear();
        await Assert.That(second.Content.Count).IsEqualTo(1);
        await Assert.That(element.GetProperty(McpOutputTestData.RequestIdField).GetGuid()).IsEqualTo(McpOutputTestData.ExecutionId);
        owner.Dispose();
        owner.Dispose();
        await Assert.That(borrowed.Span.SequenceEqual(new byte[borrowed.Length])).IsTrue();
        Assert.ThrowsExactly<ObjectDisposedException>(() => element.GetRawText());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.ToolResult());
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = owner.Bytes);
    }

    /// <summary>The inclusive capacity applies to the complete wrapper, and overflow cannot reflect canonical content.</summary>
    [Test]
    public async Task CompleteWrapperHasExactByteBoundary()
    {
        var canonical = Encoding.UTF8.GetBytes(McpOutputTestData.CanonicalJson);
        using var measured = McpReplyOwner.Success(canonical, McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution());
        using var exact = McpReplyOwner.Success(canonical, McpOutputTestData.ExecutionId, measured.Bytes.Length, UnitMcpOptions.Execution());
        await Assert.That(exact.Bytes.Span.SequenceEqual(measured.Bytes.Span)).IsTrue();
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpReplyOwner.Success(canonical, McpOutputTestData.ExecutionId, measured.Bytes.Length - 1, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).DoesNotContain(McpOutputTestData.Marker);
        using var failure = McpReplyOwner.Failure(ErrorCode.Validation, null, McpOutputTestData.MaximumBytes);
        using var failureExact = McpReplyOwner.Failure(ErrorCode.Validation, null, failure.Bytes.Length);
        await Assert.That(failureExact.Bytes.Span.SequenceEqual(failure.Bytes.Span)).IsTrue();
        var failureError = Assert.ThrowsExactly<KeyLoadException>(() => McpReplyOwner.Failure(ErrorCode.Validation, null, failure.Bytes.Length - 1));
        await Assert.That(failureError.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Invalid canonical framing and excessive nesting fail before a wrapper owner is published.</summary>
    [Test]
    public async Task InvalidCanonicalReplyCannotBecomeStructuredContent()
    {
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => McpReplyOwner.Success(            Encoding.UTF8.GetBytes(McpOutputTestData.InvalidJson), McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution()));
        var deep = Assert.ThrowsExactly<KeyLoadException>(() => McpReplyOwner.Success(            McpResponseBoundaryTestData.NestedArrays(McpOutputTestData.OverReplyDepth), McpOutputTestData.ExecutionId, McpOutputTestData.MaximumBytes, UnitMcpOptions.Execution()));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(deep.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(invalid.Message).DoesNotContain(McpOutputTestData.Marker);
    }
}

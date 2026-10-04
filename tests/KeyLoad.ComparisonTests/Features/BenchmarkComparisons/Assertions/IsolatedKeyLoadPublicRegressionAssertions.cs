using System.Text;
using System.Text.Json;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Asserts actual public envelopes, preserving null values and never printing private problem contents.</summary>
internal static class IsolatedKeyLoadPublicRegressionAssertions
{
    private const string ResultField = "result";
    private const string ErrorField = "error";
    private const string RequestIdField = "requestId";
    private const string TypeField = "type";
    private const string TitleField = "title";
    private const string StatusField = "status";
    private const string DetailField = "detail";
    private const string ErrorCodeField = "errorCode";
    private const int SummaryByteLimit = 1_024;

    internal static async Task<T> SuccessAsync<T>(Result<T> result)
    {
        await Assert.That(result.IsSuccess).IsTrue();
        return result.Value!;
    }

    internal static async Task ErrorAsync<T>(Result<T> result, ErrorCode code, string? credential = null, string? privateValue = null)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await OmissionAsync(JsonSerializer.Serialize(result.Problem, JsonDefaults.Options), credential, privateValue);
    }

    internal static async Task<T> McpSuccessAsync<T>(CallToolResult result)
    {
        await Assert.That(result.IsError is true).IsFalse();
        var envelope = result.StructuredContent ?? throw new InvalidOperationException("The official MCP reply has no structured content.");
        await KeysAsync(envelope, [ResultField, RequestIdField]);
        await Assert.That(envelope.GetProperty(RequestIdField).GetGuid()).IsNotEqualTo(Guid.Empty);
        await SummaryAsync(result);
        return envelope.GetProperty(ResultField).Deserialize<T>(JsonDefaults.Options)!;
    }

    internal static async Task McpErrorAsync(CallToolResult result, ErrorCode code, string? credential, string? privateValue)
    {
        await Assert.That(result.IsError is true).IsTrue();
        var envelope = result.StructuredContent ?? throw new InvalidOperationException("The official MCP error has no structured content.");
        await KeysAsync(envelope, [ErrorField, RequestIdField]);
        await Assert.That(envelope.GetProperty(RequestIdField).GetGuid()).IsNotEqualTo(Guid.Empty);
        var problem = envelope.GetProperty(ErrorField);
        await KeysAsync(problem, [TypeField, TitleField, StatusField, DetailField, ErrorCodeField]);
        await Assert.That(problem.GetProperty(ErrorCodeField).GetString()).IsEqualTo(code.ToString());
        await Assert.That(problem.GetProperty(StatusField).GetInt32()).IsEqualTo(Errors.Status(code));
        await SummaryAsync(result);
        await OmissionAsync(JsonSerializer.Serialize(result, JsonDefaults.Options), credential, privateValue);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task DocumentAsync(DocumentResult document, EntityRef reference, string json, long revision)
    {
        await Assert.That(document.Reference).IsEqualTo(reference);
        await Assert.That(document.Revision).IsEqualTo(revision);
        await Assert.That(document.Json).IsEqualTo(json);
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
    }

    internal static async Task ReceiptAsync(CommitReceipt receipt, Guid commandId, int mutations, PartitionRef partition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(mutations);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Token.OwnershipEpoch).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    private static async Task KeysAsync(JsonElement value, string[] expected)
    {
        await Assert.That(value.ValueKind).IsEqualTo(JsonValueKind.Object);
        var names = value.EnumerateObject().Select(item => item.Name).ToArray();
        await Assert.That(names.Length).IsEqualTo(expected.Length);
        await Assert.That(names.ToHashSet(StringComparer.Ordinal).SetEquals(expected)).IsTrue();
    }

    private static async Task SummaryAsync(CallToolResult reply)
    {
        await Assert.That(reply.Content.Count).IsGreaterThan(0);
        await Assert.That(reply.Content.All(item => item is TextContentBlock)).IsTrue();
        var bytes = reply.Content.Cast<TextContentBlock>().Sum(item => Encoding.UTF8.GetByteCount(item.Text));
        await Assert.That(bytes).IsGreaterThan(0);
        await Assert.That(bytes).IsLessThanOrEqualTo(SummaryByteLimit);
    }

    private static async Task OmissionAsync(string actual, string? credential, string? privateValue)
    {
        if (credential is not null)
        {
            await Assert.That(actual.Contains(credential, StringComparison.Ordinal)).IsFalse();
        }
        if (privateValue is not null)
        {
            await Assert.That(actual.Contains(privateValue, StringComparison.Ordinal)).IsFalse();
        }
    }
}

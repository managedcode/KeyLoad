using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpOwnedDiagnosticAssertions
{
    internal const string ForeignIncarnation = "The document session token belongs to another incarnation.";
    internal const string OutOfScope = "The document session token belongs to another atomic partition or placement.";
    internal const string InvalidPosition = "The document session token position must be positive.";
    internal const string FuturePosition = "The document session token is beyond the current quorum-applied cut.";
    internal const string VectorMismatch = "The declared vector profile does not match the configured field.";
    internal const string Generic = "The database operation could not be completed.";
    internal const string PrivateCanary = "private-diagnostic-canary";
    private const string Error = "error";
    private const string RequestId = "requestId";
    private const int MaximumBytes = 4096;

    internal static async Task ReplyAsync(ErrorCode code, string actualOwnedDetail, string expected)
    {
        var requestId = Guid.NewGuid();
        using var reply = McpReplyOwner.Failure(code, requestId, MaximumBytes, actualOwnedDetail);
        var tool = reply.ToolResult();
        await Assert.That(tool.IsError).IsTrue();
        var root = tool.StructuredContent!.Value;
        await Assert.That(root.GetProperty(RequestId).GetGuid()).IsEqualTo(requestId);
        using var canonical = JsonDocument.Parse(JsonDefaults.Serialize(Errors.Problem(code, expected)));
        await Assert.That(JsonElement.DeepEquals(root.GetProperty(Error), canonical.RootElement)).IsTrue();
        await Assert.That(root.GetRawText().Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task VectorReceiptAsync(CommitReceipt actual, CommandRequest command, bool projection)
    {
        const string PutDocumentKind = "putDocument";
        const string PutVectorKind = "putVector";
        const string ProjectionKind = "applyVectorProjection";
        const int ExpectedCount = 2;
        const long Revision = 2;
        await Assert.That(actual.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.Mutations.Length).IsEqualTo(ExpectedCount);
        MutationReceipt[] expected =
        [
            new(PutDocumentKind, ConfiguredVectorProfileFlow.Collection, ConfiguredVectorProfileFlow.Target, Revision),
            new(projection ? ProjectionKind : PutVectorKind, ConfiguredVectorProfileFlow.Collection, ConfiguredVectorProfileFlow.Target, Revision)
        ];
        for (var index = 0; index < ExpectedCount; index++)
        { await Assert.That(NativeSerialization.Serialize(actual.Mutations[index]).SequenceEqual(NativeSerialization.Serialize(expected[index]))).IsTrue(); }
    }
}

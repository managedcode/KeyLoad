using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventAppendClusterAssertions
{
    internal static async Task ReceiptAsync(CommitReceipt receipt, CommandRequest command, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(EventAppendClusterProtocol.InitialRevision);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations.SequenceEqual(new[] { new MutationReceipt(EventAppendClusterProtocol.AppendKind,
            McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, revision) })).IsTrue();
    }

    internal static async Task<StreamPage> PageAsync(KeyLoadClient sdk, McpOfficialClient official,
        McpEventStreamScenario scenario, EventData[] expected, CancellationToken token)
    {
        var request = scenario.ReadRequest(EventAppendClusterProtocol.InitialRevision, EventAppendClusterProtocol.PageLimit);
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, token));
        var second = (await McpCallerAssertions.SuccessAsync<StreamPage>(await official.CallAsync(
            McpCallerTools.StreamsRead, request, token))).Value;
        await Assert.That(first.Stream).IsEqualTo(scenario.Stream);
        await Assert.That(second.Stream).IsEqualTo(scenario.Stream);
        await Assert.That(first.Head).IsEqualTo(new StreamHead(expected.Length,
            EventAppendClusterProtocol.FirstRevision, McpEventStreamTokens.StreamGeneration));
        await Assert.That(second.Head).IsEqualTo(first.Head);
        await Assert.That(first.HasMore).IsFalse();
        await Assert.That(second.HasMore).IsFalse();
        await Assert.That(first.CutPosition).IsGreaterThan(0L);
        await Assert.That(second.CutPosition).IsGreaterThan(0L);
        await Assert.That(first.SnapshotCutPosition).IsEqualTo(first.CutPosition);
        await Assert.That(second.SnapshotCutPosition).IsEqualTo(second.CutPosition);
        await Assert.That(first.Cursor).IsNull();
        await Assert.That(second.Cursor).IsNull();
        await Assert.That(first.Events.Select(row => row.Data).SequenceEqual(expected)).IsTrue();
        await Assert.That(first.Events.All(row => row.Stream == scenario.Stream && row.RecordedAt != default)).IsTrue();
        var revisions = Enumerable.Range(McpEventStreamTokens.FirstEventRevision, expected.Length).Select(value => (long)value).ToArray();
        await Assert.That(first.Events.Select(row => row.Revision).SequenceEqual(revisions)).IsTrue();
        await Assert.That(first.Events.Select(row => row.EventSequence).SequenceEqual(revisions)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(first.Events).AsSpan().SequenceEqual(JsonDefaults.Serialize(second.Events))).IsTrue();
        return second;
    }

    internal static async Task PrefixAsync(StreamPage original, StreamPage later)
    {
        await Assert.That(JsonDefaults.Serialize(original.Events).AsSpan().SequenceEqual(
            JsonDefaults.Serialize(later.Events.Take(original.Events.Length).ToArray()))).IsTrue();
    }

    internal static async Task ReceiptIdentityAsync(CommitReceipt original, CommitReceipt later)
    {
        await Assert.That(later.Token.Incarnation).IsEqualTo(original.Token.Incarnation);
        await Assert.That(later.Token.AtomicPartitionId).IsEqualTo(original.Token.AtomicPartitionId);
        await Assert.That(later.PersistedCopies).IsEqualTo(3);
        await Assert.That(later.Token.Position).IsGreaterThan(original.Token.Position);
    }

    internal static async Task SameStreamAsync(StreamPage original, StreamPage later)
    {
        await Assert.That(later.Stream).IsEqualTo(original.Stream);
        await Assert.That(later.Head).IsEqualTo(original.Head);
        await Assert.That(later.HasMore).IsEqualTo(original.HasMore);
        await Assert.That(later.CutPosition).IsGreaterThan(0L);
        await Assert.That(JsonDefaults.Serialize(original.Events).AsSpan().SequenceEqual(JsonDefaults.Serialize(later.Events))).IsTrue();
    }

    internal static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient official, CommandRequest command,
        CommitReceipt original, CancellationToken token)
    {
        var first = await EventAppendClusterCalls.SdkAsync(sdk, command, token);
        var second = await EventAppendClusterCalls.McpAsync(official, command, token);
        await Assert.That(first.Error).IsNull();
        await Assert.That(second.Error).IsNull();
        await Assert.That(JsonDefaults.Serialize(first.Receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(JsonDefaults.Serialize(second.Receipt).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
    }

    internal static async Task RejectedAsync(KeyLoadClient sdk, McpOfficialClient official, CommandRequest command,
        ErrorCode code, string detail, string credential, CancellationToken token)
    {
        var first = await EventAppendClusterCalls.SdkAsync(sdk, command, token);
        var second = await EventAppendClusterCalls.McpAsync(official, command, token);
        await FailureAsync(first, code, detail, credential);
        await FailureAsync(second, code, detail, credential, official: true);
        var sdkReplay = await EventAppendClusterCalls.SdkAsync(sdk, command, token);
        var mcpReplay = await EventAppendClusterCalls.McpAsync(official, command, token);
        await FailureAsync(sdkReplay, code, detail, credential);
        await FailureAsync(mcpReplay, code, detail, credential, official: true);
        await Assert.That(sdkReplay.ProblemJson).IsEqualTo(first.ProblemJson);
        await Assert.That(mcpReplay.ProblemJson).IsEqualTo(second.ProblemJson);
    }

    internal static async Task FailureAsync(EventAppendClusterOutcome result, ErrorCode code, string detail, string credential, bool official = false)
    {
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(result.Error).IsEqualTo(code);
        const string GenericOfficialFailure = "The database operation could not be completed.";
        var expectedDetail = official ? (code, detail) switch
        {
            (ErrorCode.PermissionDenied, EventAppendClusterProtocol.DeniedDetail) => EventAppendClusterProtocol.DeniedDetail,
            (ErrorCode.Conflict, EventAppendClusterProtocol.CommandDetail) => EventAppendClusterProtocol.CommandDetail,
            _ => GenericOfficialFailure
        } : detail;
        await Assert.That(result.Detail).IsEqualTo(expectedDetail);
        await Assert.That(result.ProblemJson).IsNotNull();
        using var problem = JsonDocument.Parse(result.ProblemJson!);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(code, expectedDetail), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected, problem.RootElement)).IsTrue();
        await Assert.That(result.ProblemJson!.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.ProblemJson.Contains(McpEventStreamTokens.PrivateMarker, StringComparison.Ordinal)).IsFalse();
    }
}

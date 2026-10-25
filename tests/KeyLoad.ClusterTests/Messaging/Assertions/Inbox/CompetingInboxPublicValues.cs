using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;
namespace KeyLoad.ClusterTests.Messaging;

internal static class CompetingInboxPublicValues
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static async Task ReceiptAsync(CompetingInboxPublicState state, CommitReceipt actual, Guid id,
        ImmutableArray<MutationReceipt> mutations)
    {
        var expected = new CommitReceipt(id, new(actual.Token.Incarnation, state.Partition.AtomicPartitionId,
            actual.Token.Position), mutations, DurabilityProfile.ProcessDurable, 3);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await EqualAsync(expected, actual);
    }
    internal static ImmutableArray<MutationReceipt> Effects(string input, string effect)
        => [new(CompetingInboxPublicProtocol.PutKind, CompetingInboxPublicProtocol.Documents, effect, 1L),
            new(CompetingInboxPublicProtocol.AppendKind, CompetingInboxPublicProtocol.Streams, effect, 1L),
            new(CompetingInboxPublicProtocol.EnqueueKind, CompetingInboxPublicProtocol.Output, effect, 1L),
            new(CompetingInboxPublicProtocol.AckKind, CompetingInboxPublicProtocol.Input, input, 3L)];
    internal static async Task SdkFailureAsync(CompetingInboxPublicState state, Result<CommitReceipt> actual,
        ErrorCode code, string detail)
    {
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        await EqualAsync(Errors.Problem(code, detail), actual.Problem);
        var json = JsonSerializer.Serialize(actual, JsonDefaults.Options);
        await Assert.That(json.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(CompetingInboxPublicProtocol.Canary, StringComparison.Ordinal)).IsFalse();
    }
    internal static async Task McpFailureAsync(CompetingInboxPublicState state, CallToolResult actual, ErrorCode code)
    {
        _ = (await McpCallerAssertions.ErrorAsync(actual, code, dispatched: true)).Value;
        var detail = code == ErrorCode.PermissionDenied ? CompetingInboxPublicProtocol.SafeDenied : CompetingInboxPublicProtocol.SafeFailure;
        await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(Errors.Problem(code, detail), JsonDefaults.Options),
            actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(actual, state.Identity.Secret, CompetingInboxPublicProtocol.Canary);
    }
}

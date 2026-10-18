using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RequiredProcessingPolicyAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static Task ObserveAsync(ZoneTreeStore store, Func<Task> action) => GraphLabelUseOperations.ObserveAsync(store, action);
    internal static byte[] Outcome(ZoneTreeStore store, PartitionRef partition, string principal, Guid id)
        => store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(partition, principal, id)))
            ?? throw new InvalidOperationException("The original genuine processing outcome is missing.");
    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        foreach (var receipt in actual.Mutations)
        {
            await Assert.That(receipt.CompositionReferences).IsEmpty();
        }
    }
    internal static async Task FailureAsync(Result<object> actual, ErrorCode code, string detail)
    {
        await EqualAsync(Result<object>.Fail(Errors.Problem(code, detail)), actual);
        await Assert.That(actual.Value).IsNull();
    }
    internal static Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, Command<DatabaseCommand> operation,
        CommitReceipt receipt, byte[] outcome, PartitionRef partition, CancellationToken token)
        => ObserveAsync(store, async () =>
        {
            await ReceiptAsync(receipt, database.ApplyEmbedded(operation, token).Get<CommitReceipt>());
            await Assert.That(Outcome(store, partition, operation.UserId!, operation.CommandId).AsSpan().SequenceEqual(outcome)).IsTrue();
        });
}

using System.Collections.Immutable;
using System.Reflection;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

internal static class SubmissionColdAssertions
{
    private const string ReferencesProperty = "CompositionReferences";
    private const string MissingReferencesContract = "The execution receipt privacy contract is missing.";
    private static readonly PropertyInfo PrivateReferences = typeof(MutationReceipt)
        .GetProperty(ReferencesProperty, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(MissingReferencesContract);

    internal static async Task ReplayAsync(RequestCqrsClusterCallers caller, CommandRequest command,
        CommitReceipt original, CancellationToken token)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token).ConfigureAwait(false))
            .ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false)).ConfigureAwait(false);
        await ReceiptAsync(sdk, original).ConfigureAwait(false);
        await ReceiptAsync(mcp.Value, original).ConfigureAwait(false);
    }

    internal static async Task ReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true }).IsTrue();
        }
        foreach (var mutation in expected.Mutations)
        {
            await Assert.That(PrivateReferences.GetValue(mutation) is ImmutableArray<EntityRef> { IsEmpty: true }).IsTrue();
        }
    }

    internal static async Task AtomicPartitionAsync(CommandRequest original, CommitReceipt receipt)
    {
        await Assert.That(original.Partition.AtomicPartitionId).IsEqualTo(receipt.Token.AtomicPartitionId);
    }
}

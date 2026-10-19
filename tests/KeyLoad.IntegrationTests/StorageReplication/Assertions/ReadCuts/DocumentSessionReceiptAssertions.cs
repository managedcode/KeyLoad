using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using static KeyLoad.IntegrationTests.Features.StorageReplication.DocumentSessionReadClusterProtocol;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Checks literal complete operations and explicit execution authority rather than health-only readiness.</summary>
internal static class DocumentSessionReceiptAssertions
{
    private const string Mutation = "putDocument";
    internal static async Task ReceiptAsync(CommitReceipt receipt, CommandRequest command, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Token.Position > Zero).IsTrue();
        await Assert.That(receipt.Token.Incarnation != Guid.Empty).IsTrue();
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(First);
        await Assert.That(OrleansSerialization.Serialize(receipt.Mutations.Single()).SequenceEqual(
            OrleansSerialization.Serialize(new MutationReceipt(Mutation, Collection, DocumentId, revision)))).IsTrue();
    }

    internal static async Task ReplayedAsync(KeyLoadClient client, McpOfficialClient official,
        CommandRequest command, CommitReceipt receipt, CancellationToken cancellationToken)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(
            await client.OutboxStatusAsync(command.Partition, cancellationToken));
        await EqualReceiptAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, cancellationToken)));
        await EqualReceiptAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await official.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken))).Value);
        var call = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await EqualReceiptAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(client, call, cancellationToken));
        await EqualReceiptAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(official, call, cancellationToken));
        await SqlClusterProtocol.EqualAsync(before, await McpCallerAssertions.SdkSuccessAsync(
            await client.OutboxStatusAsync(command.Partition, cancellationToken)));
    }

    private static async Task EqualReceiptAsync(CommitReceipt original, CommitReceipt actual)
        => await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(
            OrleansSerialization.Serialize(original))).IsTrue();
}

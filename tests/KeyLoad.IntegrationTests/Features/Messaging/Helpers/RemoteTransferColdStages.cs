using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdStages
{
    internal static async Task<(CommitReceipt Receipt, QueueTransferInspection Intent)> CreateAsync(
        KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed.Create, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(receipt, seed.Create, seed.Scenario.SourceQueue,
            seed.TransferId, RemoteTransferColdProtocol.CreateKind, RemoteTransferColdProtocol.CreateRevision);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, seed.Create, receipt, token);
        var intent = await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, null, token);
        await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(intent.ReceiptToken).IsNull();
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, null, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, null, token);
        return (receipt, intent);
    }

    internal static async Task<(CommitReceipt Receipt, QueueTransferReceiptInspection Proof)> AcceptAsync(
        KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CommandRequest command, long sequence, CancellationToken token)
    {
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(receipt, command, seed.Scenario.DestinationQueue,
            seed.TransferId, RemoteTransferColdProtocol.AcceptKind, receipt.Token.Position);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, command, receipt, token);
        var proof = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferColdProtocol.Missing);
        await Assert.That(proof.SourceQueue).IsEqualTo(seed.Scenario.SourceQueue);
        await Assert.That(proof.DestinationQueue).IsEqualTo(seed.Scenario.DestinationQueue);
        await Assert.That(proof.TransferId).IsEqualTo(seed.TransferId);
        await SqlRf3Protocol.EqualAsync(receipt.Token, proof.TargetCommit);
        await Assert.That(string.IsNullOrEmpty(proof.ReceiptToken)).IsFalse();
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
            RemoteTransferColdAssertions.Ready(seed, sequence), token);
        return (receipt, proof);
    }

    internal static async Task<CommitReceipt> CompleteAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferInspection intent, QueueTransferReceiptInspection proof,
        CommandRequest command, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(receipt, command, seed.Scenario.SourceQueue,
            seed.TransferId, RemoteTransferColdProtocol.CompleteKind, RemoteTransferColdProtocol.CompleteRevision);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, command, receipt, token);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed,
            intent with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, token);
        return receipt;
    }
}

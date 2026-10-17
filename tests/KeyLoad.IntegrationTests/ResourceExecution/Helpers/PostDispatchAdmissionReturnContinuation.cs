using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class PostDispatchAdmissionReturnContinuation
{
    private const long FirstOwnershipEpoch = 1;
    private const long BeforeFirstCommit = 0;
    private const long OneEffect = 1;

    internal static async Task<PostDispatchAdmissionReturnOutcome> FirstCommitAsync(ConnectionRf3Scenario scenario,
        PostDispatchAdmissionReturnState original, CancellationToken token)
    {
        var caller = original.Disconnected ? await scenario.CallerAsync(token, multiplexed: true) : original.OriginalCaller;
        var committed = await scenario.ObserveAsync(scenario.Identity, original.Command.CommandId, null,
            () => caller.Sdk.CommitAsync(original.Command, token), token);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(committed.Result);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(scenario.Discovery[0].Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(scenario.Identity.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(FirstOwnershipEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(BeforeFirstCommit);
        var expected = new CommitReceipt(original.Command.CommandId, receipt.Token,
            [new("putDocument", RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId, 2L)],
            DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(receipt, expected);
        if (original.Disconnected)
        {
            await Assert.That(committed.Witness.ConnectionId).IsNotEqualTo(original.OriginalOwner.ConnectionId);
            await Assert.That(committed.Witness.ActivationId).IsNotEqualTo(original.OriginalOwner.ActivationId);
        }
        else
        {
            await ConnectionRf3WitnessReader.SameOwnerAsync(original.OriginalOwner, committed.Witness);
        }
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.OutboxStatusAsync(scenario.Identity.Partition, token));
        await Assert.That(outbox.Head.Tail).IsEqualTo(original.Before.Head.Tail + OneEffect);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(original.Before.Head.StoredRecords + OneEffect);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(original.Before.Head.FirstAvailable);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(original.Before.Head.StoredBytes);
        await EqualAsync(outbox.Consumers, original.Before.Consumers);
        return new(receipt, outbox);
    }

    internal static async Task ReplayAsync(ConnectionRf3Scenario scenario, PostDispatchAdmissionReturnState original,
        PostDispatchAdmissionReturnOutcome expected, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token);
        await EqualAsync(expected.Receipt, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.CommitAsync(original.Command, token)));
        await EqualAsync(expected.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.CallAsync(McpCallerTools.DocumentsCommit, original.Command, token))).Value);
        var reference = ConnectionRf3PublicOperations.Reference(scenario.Identity);
        var document = new DocumentReadResult(new(reference, 2L, RequestCqrsRf3Protocol.ChangedDocumentJson, false, []));
        await EqualAsync(document, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.GetAsync(reference, expected.Receipt.Token, token)));
        await EqualAsync(document, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await caller.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, expected.Receipt.Token), token))).Value);
        await EqualAsync(expected.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await scenario.Administrator.OutboxStatusAsync(scenario.Identity.Partition, token)));
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}

internal sealed record PostDispatchAdmissionReturnOutcome(CommitReceipt Receipt, OutboxStatus Outbox);

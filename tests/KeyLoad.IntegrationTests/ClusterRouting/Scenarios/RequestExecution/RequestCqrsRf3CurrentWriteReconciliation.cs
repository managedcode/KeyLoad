using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3CurrentWriteReconciliation
{
    private const string MutationKind = "putDocument";
    private const string UnknownObservation = "C1 current write observed UnknownWriteOutcome for command ";

    internal static async Task<CommitReceipt> CommitAsync(KeyLoadClient administrator, CommandRequest command,
        CancellationToken cancellationToken)
    {
        var original = await administrator.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        var observed = original;
        if (!observed.IsSuccess && observed.Problem?.ErrorCode == nameof(ErrorCode.UnknownWriteOutcome))
        {
            TestContext.Current?.Output.WriteLine(UnknownObservation + command.CommandId.ToString("D"));
            cancellationToken.ThrowIfCancellationRequested();
            observed = await administrator.CommitAsync(command, cancellationToken).ConfigureAwait(false);
        }
        var receipt = await McpCallerAssertions.SdkSuccessAsync(observed).ConfigureAwait(false);
        var document = (PutDocument)command.Mutations.Single();
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(1);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt(MutationKind, document.Collection, document.Id, 2));
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        return receipt;
    }
}

using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterReplication.NativeSubmissionColdProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionColdHealthy
{
    internal static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers caller, RequestCqrsRf3Callers administrator,
        CommandRequest command, CommitReceipt original, AtomicPartitionPlacementResolution placement, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token).ConfigureAwait(false))
            .ConfigureAwait(false);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token).ConfigureAwait(false))
            .ConfigureAwait(false);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(placement.Partition).IsEqualTo(command.Partition);
        await Assert.That(before.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(after.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.Position).IsGreaterThan(before.Applied);
        await Assert.That(receipt.Token.Position).IsGreaterThan(original.Token.Position);
        await Assert.That(receipt.Token.Position).IsLessThanOrEqualTo(after.Applied);
        var expected = new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new(P.PutKind, RequestCqrsRf3Protocol.AdminCollection, P.HealthyDocument, P.HealthyRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await NativeSubmissionColdAssertions.ReceiptAsync(receipt, expected).ConfigureAwait(false);
        return receipt;
    }
}

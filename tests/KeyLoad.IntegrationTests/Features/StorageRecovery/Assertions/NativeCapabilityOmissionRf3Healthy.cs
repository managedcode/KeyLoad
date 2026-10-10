using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeCapabilityOmissionRf3Healthy
{
    internal static async Task CompleteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        CommandRequest command, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token).ConfigureAwait(false));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations.Single()).IsEqualTo(new MutationReceipt(NativeCapabilityOmissionRf3Protocol.PutKind,
            RelationalSqlRf3Tokens.Documents, NativeCapabilityOmissionRf3Protocol.DocumentId, NativeCapabilityOmissionRf3Protocol.Revision));
        await RequireReceiptAsync(seed, command, receipt, token).ConfigureAwait(false);
        await RequireDocumentAsync(seed, token).ConfigureAwait(false);
        await seed.VerifyAsync(token).ConfigureAwait(false);
        _ = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token).ConfigureAwait(false);
        await RequireReceiptAsync(seed, command, receipt, token).ConfigureAwait(false);
        await RequireDocumentAsync(seed, token).ConfigureAwait(false);
        await seed.VerifyAsync(token).ConfigureAwait(false);
    }
    private static async Task RequireReceiptAsync(PartitionMovementPublicParentRf3Seed seed,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.CommitAsync(command, token).ConfigureAwait(false)));
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, command);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, token));
    }
    private static async Task RequireDocumentAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents, NativeCapabilityOmissionRf3Protocol.DocumentId);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.GetAsync(reference, token).ConfigureAwait(false));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.Reference).IsEqualTo(reference);
        await Assert.That(actual.Revision).IsEqualTo(NativeCapabilityOmissionRf3Protocol.Revision);
        await Assert.That(actual.Json).IsEqualTo(NativeCapabilityOmissionRf3Protocol.DocumentJson);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
        await SqlRf3Protocol.EqualAsync(actual, (await McpCallerAssertions.SuccessAsync<DocumentResult?>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token).ConfigureAwait(false))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference));
        await SqlRf3Protocol.EqualAsync(actual, await SqlRf3Protocol.SdkAsync<DocumentResult?>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(actual, await SqlRf3Protocol.McpAsync<DocumentResult?>(seed.Official, sql, token));
    }
}

using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3Replay
{
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    private const int FirstMutation = 0;

    internal static async Task AllAsync(KeyLoadClient source, KeyLoadClient destination,
        McpOfficialClient sourceOfficial, McpOfficialClient destinationOfficial,
        DistributedSearchRf3Seed seed, CancellationToken token)
    {
        await RemotePartitionQueryRf3Assertions.LocalReceiptAsync(source, sourceOfficial, seed.Original, token);
        await RemotePartitionQueryRf3Assertions.ReceiptReplayAsync(destination, destinationOfficial, seed.Original, token);
        await OneAsync(source, sourceOfficial, seed.SourceDocuments, seed.SourceDocumentsReceipt,
            [new(PutDocumentKind, RemoteDocumentRf3Protocol.Collection, DistributedSearchRf3Seed.LongId, DistributedSearchRf3Seed.Revision),
             new(PutDocumentKind, RemoteDocumentRf3Protocol.Collection, DistributedSearchRf3Seed.MissingId, DistributedSearchRf3Seed.Revision)], token);
        await OneAsync(source, sourceOfficial, seed.SourceVector, seed.SourceVectorReceipt,
            [new(PutVectorKind, RemoteDocumentRf3Protocol.Collection, DistributedSearchRf3Seed.LongId, DistributedSearchRf3Seed.Revision)], token);
        await OneAsync(destination, destinationOfficial, seed.DestinationVector, seed.DestinationVectorReceipt,
            [new(PutVectorKind, RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document, DistributedSearchRf3Seed.Revision)], token);
    }

    private static async Task OneAsync(KeyLoadClient administrator, McpOfficialClient official,
        CommandRequest request, CommitReceipt original, MutationReceipt[] expected, CancellationToken token)
    {
        await Assert.That(original.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(original.Token.AtomicPartitionId).IsEqualTo(request.Partition.AtomicPartitionId);
        await Assert.That(original.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(original.Mutations.Length).IsEqualTo(expected.Length);
        for (var index = FirstMutation; index < expected.Length; index++)
        {
            await Assert.That(NativeSerialization.Serialize(original.Mutations[index]).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(expected[index]))).IsTrue();
        }
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
            RemoteDocumentRf3Protocol.DocumentsCommit, request, token))).Value;
        await Assert.That(NativeSerialization.Serialize(sdk).AsSpan().SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(mcp).AsSpan().SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
    }
}

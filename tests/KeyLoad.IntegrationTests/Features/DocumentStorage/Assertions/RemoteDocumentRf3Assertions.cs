using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class RemoteDocumentRf3Assertions
{
    private const int OnlyMutation = 0;
    private const int MutationCount = 1;
    private const long EmptyPosition = 0;
    private const string PutKind = "putDocument";
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";

    internal static async Task ReceiptAsync(RemoteDocumentRf3Seed seed)
    {
        var receipt = seed.Receipt;
        var destination = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId != seed.Directory.ControlOwner.PhysicalShardId);
        await Assert.That(receipt.CommandId).IsEqualTo(seed.Command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(destination.Owner.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(RemoteDocumentRf3Protocol.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(destination.Owner.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(EmptyPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(MutationCount);
        var expected = new MutationReceipt(PutKind, RemoteDocumentRf3Protocol.Collection,
            RemoteDocumentRf3Protocol.Document, RemoteDocumentRf3Protocol.FirstRevision);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations[OnlyMutation])
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task DeniedAsync(KeyLoadClient reader, CancellationToken token)
    {
        var result = await reader.GetAsync(RemoteDocumentRf3Protocol.Reference, token).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        var problem = result.Problem ?? throw new InvalidOperationException("The real remote read denial has no problem.");
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(problem, JsonDefaults.Options), ErrorCode.PermissionDenied);
        await Assert.That(problem.Detail).IsEqualTo(DeniedDetail);
    }

    internal static async Task DocumentAsync(DocumentResult? actual, bool projected)
    {
        var expected = new DocumentResult(RemoteDocumentRf3Protocol.Reference, RemoteDocumentRf3Protocol.FirstRevision,
            projected ? RemoteDocumentRf3Protocol.ProjectedJson : RemoteDocumentRf3Protocol.OriginalJson,
            projected, projected ? [RemoteDocumentRf3Protocol.Secret] : []);
        await Assert.That(actual is not null).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task ReplayAsync(KeyLoadClient destination, McpOfficialClient session,
        RemoteDocumentRf3Seed seed, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var replay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(
            RemoteDocumentRf3Protocol.DocumentsCommit, seed.Command, token))).Value;
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(seed.Receipt))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
        await DocumentAsync(await McpCallerAssertions.SdkSuccessAsync(await destination.GetAsync(RemoteDocumentRf3Protocol.Reference, token)), projected: false);
    }
}

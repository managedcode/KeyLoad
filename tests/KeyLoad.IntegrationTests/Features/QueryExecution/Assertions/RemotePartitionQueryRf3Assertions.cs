using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class RemotePartitionQueryRf3Assertions
{
    private const int FirstLeafIndex = 0;
    private const string MissingProblem = "The remote query denial has no problem.";
    private const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    private const long FirstRevision = 1;
    private const long InitialPosition = 0;
    private const int LeafCount = 2;
    private const string CancelledDetail = "The read response is unavailable.";
    private const int OnlyMutation = 0;
    private const int MutationCount = 1;
    private const string PutKind = "putDocument";
    private const string AccessPath = "bounded-full-scan";

    internal static async Task DeniedAsync(KeyLoadClient reader, CancellationToken token)
    {
        var result = await reader.PartitionQueryAsync(RemotePartitionQueryRf3Seed.Request(), token).ConfigureAwait(false);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
        var problem = result.Problem ?? throw new InvalidOperationException(MissingProblem);
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(problem, JsonDefaults.Options),
            ErrorCode.PermissionDenied);
        await Assert.That(problem.Detail).IsEqualTo(DeniedDetail);
    }

    internal static async Task PageAsync(PartitionQueryPageV1 actual)
    {
        var expectedRows = new PartitionQueryRowV1[]
        {
            new(RemoteDocumentRf3Protocol.Reference, new(RemoteDocumentRf3Protocol.Document,
                FirstRevision, RemoteDocumentRf3Protocol.ProjectedJson, true, [RemoteDocumentRf3Protocol.Secret])),
            new(new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection,
                RemoteDocumentRf3Protocol.Document), new(RemoteDocumentRf3Protocol.Document,
                FirstRevision, RemotePartitionQueryRf3Seed.SourceProjected, true, [RemoteDocumentRf3Protocol.Secret]))
        };
        await Assert.That(actual.Version).IsEqualTo(RemotePartitionQueryRf3Seed.Version);
        await Assert.That(actual.Complete).IsTrue();
        await Assert.That(JsonDefaults.Serialize(actual.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedRows))).IsTrue();
        await Assert.That(actual.Leaves.Length).IsEqualTo(LeafCount);
        var expectedPartitions = new[] { RemoteDocumentRf3Protocol.Partition, RemotePartitionQueryRf3Seed.Local };
        for (var index = FirstLeafIndex; index < actual.Leaves.Length; index++)
        {
            var leaf = actual.Leaves[index];
            await Assert.That(leaf.Partition).IsEqualTo(expectedPartitions[index]);
            await Assert.That(leaf.CutPosition >= InitialPosition).IsTrue();
            await Assert.That(leaf.PolicyEpoch).IsEqualTo(RemotePartitionQueryRf3Seed.GrantedEpoch);
            await Assert.That(leaf.SchemaVersion).IsEqualTo(FirstRevision);
            await Assert.That(leaf.AccessPath).IsEqualTo(AccessPath);
        }
    }

    internal static async Task CancelledAsync(KeyLoadClient reader, CancellationToken token)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await caller.CancelAsync().ConfigureAwait(false);
        var actual = await reader.PartitionQueryAsync(RemotePartitionQueryRf3Seed.Request(), caller.Token).ConfigureAwait(false);
        await Assert.That(actual.IsSuccess).IsFalse();
        await Assert.That(actual.Value).IsNull();
        var problem = actual.Problem ?? throw new InvalidOperationException(MissingProblem);
        await McpCallerAssertions.VerifyProblemAsync(JsonSerializer.SerializeToElement(problem, JsonDefaults.Options), ErrorCode.Cancelled);
        await Assert.That(problem.Detail).IsEqualTo(CancelledDetail);
    }

    internal static async Task LocalReceiptAsync(KeyLoadClient source, McpOfficialClient official,
        RemotePartitionQueryRf3Seed seed, CancellationToken token)
    {
        var receipt = seed.LocalReceipt;
        var owner = seed.Destination.Directory.ControlOwner;
        await Assert.That(receipt.CommandId).IsEqualTo(seed.LocalCommand.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(owner.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(RemotePartitionQueryRf3Seed.Local.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(owner.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(InitialPosition);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(MutationCount);
        var expected = new MutationReceipt(PutKind, RemoteDocumentRf3Protocol.Collection,
            RemoteDocumentRf3Protocol.Document, FirstRevision);
        await Assert.That(NativeSerialization.Serialize(receipt.Mutations[OnlyMutation])
            .AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        var before = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(seed.LocalCommand, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
            RemoteDocumentRf3Protocol.DocumentsCommit, seed.LocalCommand, token))).Value;
        await Assert.That(NativeSerialization.Serialize(sdk).AsSpan().SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(mcp).AsSpan().SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        await Assert.That(after.Applied).IsEqualTo(before.Applied);
        var document = await McpCallerAssertions.SdkSuccessAsync(await source.GetAsync(
            new(RemotePartitionQueryRf3Seed.Local, RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document), token));
        var literal = new DocumentResult(new(RemotePartitionQueryRf3Seed.Local,
            RemoteDocumentRf3Protocol.Collection, RemoteDocumentRf3Protocol.Document), FirstRevision,
            RemotePartitionQueryRf3Seed.SourceJson, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
    }

    internal static async Task ReceiptReplayAsync(KeyLoadClient destination, McpOfficialClient official,
        RemotePartitionQueryRf3Seed seed, CancellationToken token)
    {
        await RemoteDocumentRf3Assertions.ReceiptAsync(seed.Destination).ConfigureAwait(false);
        await RemoteDocumentRf3Assertions.ReplayAsync(destination, official, seed.Destination, token).ConfigureAwait(false);
    }
}

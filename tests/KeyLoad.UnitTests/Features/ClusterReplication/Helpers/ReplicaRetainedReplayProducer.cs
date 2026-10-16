using System.Collections.Immutable;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaRetainedReplayProducer
{
    internal const string HealthyDocument = "replay-healthy";
    internal const string HealthyJson = "{\"value\":2}";
    internal const string FollowingDocument = "replay-repaired";
    internal const string FollowingJson = "{\"value\":3}";
    private const string BlobResource = "retained-blobs";
    private const string BlobId = "original";
    private const string PutDocumentKind = "putDocument";
    internal const int Empty = 0;
    internal const int Single = 1;
    internal static readonly byte[] Bytes = [0x00, 0x31, 0xAF, 0xFF];

    internal static async Task<ReplicaRetainedReplaySeed> CreateAsync(RequestCqrsCohortNativeNode leader,
        CancellationToken token)
    {
        var commands = ImmutableArray.CreateBuilder<ReplicaRetainedReplayCommand>();
        commands.Add(await DocumentAsync(leader, P.DocumentId, P.Json, token));
        var configured = await SubmitAsync(leader, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(P.Partition.TenantId, P.Partition.DatabaseId,
                new(BlobResource, ResourceKind.BlobStore, P.Partition.TransactionDomainId)), token);
        await Assert.That(configured.Result.Error).IsNull();
        var blob = new BlobRef(P.Partition, BlobResource, BlobId);
        var upload = Guid.NewGuid();
        var id = Guid.NewGuid();
        var begin = await SubmitAsync(leader, OperationKind.BeginBlobUpload, id,
            new BeginBlobUploadRequest(id, blob, upload, Bytes.Length, Empty), token);
        commands.Add(begin);
        await ReceiptAsync(leader, begin, begin.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt);
        id = Guid.NewGuid();
        var partHash = BlobIntegrity.PartHash(Bytes);
        var write = await SubmitAsync(leader, OperationKind.WriteBlobPart, id,
            new WriteBlobPartRequest(id, blob, upload, Empty, Bytes, partHash), token);
        commands.Add(write);
        await ReceiptAsync(leader, write, write.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt);
        var chain = BlobIntegrity.NextHash(BlobIntegrity.InitialHash(leader.Journal.Configuration.Incarnation,
            blob, upload, Bytes.Length), Empty, Bytes.Length, partHash);
        id = Guid.NewGuid();
        var complete = await SubmitAsync(leader, OperationKind.CompleteBlobUpload, id,
            new CompleteBlobUploadRequest(id, blob, upload, chain), token);
        commands.Add(complete);
        var committed = complete.Result.Get<BlobCommitResult<BlobMetadata>>();
        await ReceiptAsync(leader, complete, committed.Receipt);
        var evaluated = leader.Journal.Log.ReadEntry(committed.Receipt.Token.Position)!.Operation!.EvaluatedAt;
        var expected = new BlobMetadata(blob, Single, upload, Bytes.Length, Single, chain,
            new RowAccess(), evaluated);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected),
            NativeSerialization.Serialize(committed.Value));
        return new(commands.ToImmutable(), blob, expected);
    }

    internal static async Task<ReplicaRetainedReplayCommand> DocumentAsync(RequestCqrsCohortNativeNode leader,
        string document, string json, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var expectedIndex = checked(leader.Journal.Log.State.LastIndex + Single);
        var command = await SubmitAsync(leader, OperationKind.Batch, id,
            new CommandRequest(id, P.Partition, [new PutDocument(P.Collection, document, json, Empty)]), token);
        var expected = new CommitReceipt(id, new(leader.Journal.Configuration.Incarnation,
            P.Partition.AtomicPartitionId, expectedIndex, Single),
            [new(PutDocumentKind, P.Collection, document, Single)], DurabilityProfile.ProcessDurable);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected),
            NativeSerialization.Serialize(command.Result.Get<CommitReceipt>()));
        return command;
    }

    internal static async Task<ReplicaRetainedReplayCommand> SubmitAsync<T>(RequestCqrsCohortNativeNode leader,
        OperationKind kind, Guid id, T request, CancellationToken token, string principal = P.Root)
    {
        var operation = leader.Journal.Database.CreateNativeOperation(kind, id, principal,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        var result = await leader.Journal.Consensus.SubmitAsync(operation, token);
        await Assert.That(result.Error).IsNull();
        return new(operation, result);
    }

    private static async Task ReceiptAsync(RequestCqrsCohortNativeNode leader,
        ReplicaRetainedReplayCommand command, CommitReceipt receipt)
    {
        var expected = new CommitReceipt(command.Operation.Id, new(leader.Journal.Configuration.Incarnation,
            P.Partition.AtomicPartitionId, leader.Journal.Log.State.LastIndex, Single), [], DurabilityProfile.ProcessDurable);
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected),
            NativeSerialization.Serialize(receipt));
    }
}

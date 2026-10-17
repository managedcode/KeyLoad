using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementOutcomeValueAssertions
{
    private const long OwnershipEpoch = 1;
    private const long InitialRevision = 0;
    private const long PublishedRevision = 1;
    private const int Empty = 0;
    private const int SinglePart = 1;
    private const int BlobLength = 4;
    private const int UploadTtlSeconds = 3600;

    internal static async Task RequireAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementRetainedOutcome record, DateTimeOffset beganAt)
    {
        var result = record.Outcome.Result;
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var partition = ControlledPartitionMovementCorpus.Partition;
        var receipt = new CommitReceipt(record.Operation.Id,
            new(source.Store.Identity.Incarnation, partition.AtomicPartitionId, record.Index, OwnershipEpoch),
            ImmutableArray<MutationReceipt>.Empty, DurabilityProfile.ProcessDurable);
        if (record.Index == ControlledPartitionMovementOutcomeInventory.BatchIndex)
        {
            await EqualAsync(result.Get<CommitReceipt>(), receipt with
            {
                Mutations = ControlledPartitionMovementCorpus.ExpectedMutations()
            });
            return;
        }
        if (record.Index == ControlledPartitionMovementOutcomeInventory.CompleteIndex)
        {
            var metadata = new BlobMetadata(ControlledPartitionMovementBlobSeed.Blob, PublishedRevision,
                ControlledPartitionMovementBlobSeed.UploadId, BlobLength, SinglePart,
                ControlledPartitionMovementOutcomeBlobOracle.Complete(source.Store.Identity.Incarnation),
                new RowAccess(), record.Operation.EvaluatedAt);
            await EqualAsync(result.Get<BlobCommitResult<BlobMetadata>>(), new BlobCommitResult<BlobMetadata>(receipt, metadata));
            return;
        }
        if (record.Index is ControlledPartitionMovementOutcomeInventory.BeginIndex or ControlledPartitionMovementOutcomeInventory.PartIndex)
        {
            var isBegin = record.Index == ControlledPartitionMovementOutcomeInventory.BeginIndex;
            var info = new BlobUploadInfo(ControlledPartitionMovementBlobSeed.Blob, ControlledPartitionMovementBlobSeed.UploadId,
                BlobLength, InitialRevision, isBegin ? Empty : SinglePart, isBegin ? Empty : BlobLength,
                beganAt.AddSeconds(UploadTtlSeconds), BlobUploadStatus.Active,
                isBegin ? ControlledPartitionMovementOutcomeBlobOracle.Initial(source.Store.Identity.Incarnation)
                    : ControlledPartitionMovementOutcomeBlobOracle.Complete(source.Store.Identity.Incarnation));
            await EqualAsync(result.Get<BlobCommitResult<BlobUploadInfo>>(), new BlobCommitResult<BlobUploadInfo>(receipt, info));
            return;
        }
        var expected = Resource(record.Index);
        await EqualAsync(result.Get<ResourceDefinition>(), expected);
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(record.Operation);
        await EqualAsync(request, new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, expected));
    }

    internal static ResourceDefinition Resource(long index) => index switch
    {
        ControlledPartitionMovementOutcomeInventory.CollectionIndex => new(ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection, ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        ControlledPartitionMovementOutcomeInventory.TopicIndex => new(ControlledPartitionMovementCorpus.Topic, ResourceKind.Topic, ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        ControlledPartitionMovementOutcomeInventory.QueueIndex => new(ControlledPartitionMovementCorpus.Queue, ResourceKind.WorkQueue, ControlledPartitionMovementCorpus.Partition.TransactionDomainId),
        _ => new(ControlledPartitionMovementBlobSeed.Resource, ResourceKind.BlobStore, ControlledPartitionMovementCorpus.Partition.TransactionDomainId)
    };

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.SerializeToUtf8Bytes(actual, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
}

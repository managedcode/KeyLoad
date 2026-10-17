using System.Text.Json;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.DocumentStorage;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class ScalarIndexReferenceModelAssertions
{
    private const int EmptyEvidenceBytes = 0;
    private const long MissingRevision = 0;
    private const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const long ThirdRevision = 3;
    private const string MissingOrOversizedEvidenceMessage = "The scalar-index process evidence was absent or outside its existing bound.";
    private const string InvalidEvidenceMessage = "The scalar-index process evidence could not be decoded.";
    private const string ModelMismatchMessage = "Recovered documents or scalar-index membership differed from the independent reference model.";

    internal static ScalarIndexCrashSnapshot Prepared() => Create(
        [Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.FirstId,
                ScalarIndexCrashContract.ReplacedJson, SecondRevision),
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.SecondId,
                ScalarIndexCrashContract.PatchedJson, SecondRevision),
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.DeletedId, ScalarIndexCrashContract.TombstoneJson, SecondRevision, true),
            Missing(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.ConflictId),
            Missing(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.OtherPartitionId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.FirstId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.SecondId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.DeletedId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.ConflictId),
            Document(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.OtherPartitionId,
                ScalarIndexCrashContract.ReplacedJson, FirstRevision)],
        Memberships((ScalarIndexCrashContract.Partition.PartitionKey,
                new[] { (ScalarIndexCrashContract.ValueAlpha, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueBeta, Array.Empty<string>()), (ScalarIndexCrashContract.ValueGamma, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueDelta, new[] { ScalarIndexCrashContract.FirstId }),
                    (ScalarIndexCrashContract.ValueEpsilon, new[] { ScalarIndexCrashContract.SecondId }),
                    (ScalarIndexCrashContract.ValueRollback, Array.Empty<string>()), (ScalarIndexCrashContract.ValueZeta, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueTheta, Array.Empty<string>()), (ScalarIndexCrashContract.ValueHealthy, Array.Empty<string>()) }),
            (ScalarIndexCrashContract.OtherPartition.PartitionKey,
                new[] { (ScalarIndexCrashContract.ValueAlpha, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueBeta, Array.Empty<string>()), (ScalarIndexCrashContract.ValueGamma, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueDelta, new[] { ScalarIndexCrashContract.OtherPartitionId }),
                    (ScalarIndexCrashContract.ValueEpsilon, Array.Empty<string>()), (ScalarIndexCrashContract.ValueRollback, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueZeta, Array.Empty<string>()), (ScalarIndexCrashContract.ValueTheta, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueHealthy, Array.Empty<string>()) })));

    internal static ScalarIndexCrashSnapshot Recovered() => WithPostCutDocuments(
        ScalarIndexCrashContract.ReplacedJson, ScalarIndexCrashContract.FaultPatchJson,
        ScalarIndexCrashContract.FaultInsertJson, SecondRevision);

    internal static ScalarIndexCrashSnapshot Healthy() => WithPostCutDocuments(
        ScalarIndexCrashContract.HealthyJson, ScalarIndexCrashContract.FaultPatchJson,
        ScalarIndexCrashContract.FaultInsertJson, ThirdRevision);

    internal static async Task AssertFileAsync(string root, string fileName, ScalarIndexCrashSnapshot expected)
    {
        var path = Path.Combine(root, fileName);
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is <= EmptyEvidenceBytes or > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        {
            throw new InvalidOperationException(MissingOrOversizedEvidenceMessage);
        }
        var bytes = await File.ReadAllBytesAsync(path);
        var actual = JsonSerializer.Deserialize<ScalarIndexCrashSnapshot>(bytes, JsonDefaults.Options)
            ?? throw new InvalidOperationException(InvalidEvidenceMessage);
        var expectedBytes = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options);
        if (!JsonSerializer.SerializeToUtf8Bytes(actual, JsonDefaults.Options).AsSpan().SequenceEqual(expectedBytes))
        {
            throw new InvalidOperationException(ModelMismatchMessage);
        }
    }

    private static ScalarIndexCrashSnapshot WithPostCutDocuments(string firstJson, string secondJson,
        string insertedJson, long firstRevision)
    {
        var documents = new[]
        {
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.FirstId, firstJson, firstRevision),
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.SecondId, secondJson, ThirdRevision),
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.DeletedId, ScalarIndexCrashContract.TombstoneJson, SecondRevision, true),
            Document(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.ConflictId, insertedJson, FirstRevision),
            Missing(ScalarIndexCrashContract.Partition.PartitionKey, ScalarIndexCrashContract.OtherPartitionId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.FirstId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.SecondId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.DeletedId),
            Missing(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.ConflictId),
            Document(ScalarIndexCrashContract.OtherPartition.PartitionKey, ScalarIndexCrashContract.OtherPartitionId,
                ScalarIndexCrashContract.ReplacedJson, FirstRevision)
        };
        return Create(documents, Memberships(
            (ScalarIndexCrashContract.Partition.PartitionKey,
                new[] { (ScalarIndexCrashContract.ValueAlpha, Array.Empty<string>()), (ScalarIndexCrashContract.ValueBeta, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueGamma, Array.Empty<string>()), (ScalarIndexCrashContract.ValueDelta,
                        firstJson == ScalarIndexCrashContract.ReplacedJson ? new[] { ScalarIndexCrashContract.FirstId } : Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueEpsilon, Array.Empty<string>()), (ScalarIndexCrashContract.ValueRollback, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueZeta, new[] { ScalarIndexCrashContract.ConflictId }),
                    (ScalarIndexCrashContract.ValueTheta, secondJson == ScalarIndexCrashContract.FaultPatchJson
                        ? new[] { ScalarIndexCrashContract.SecondId } : Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueHealthy, firstJson == ScalarIndexCrashContract.HealthyJson
                        ? new[] { ScalarIndexCrashContract.FirstId } : Array.Empty<string>()) }),
            (ScalarIndexCrashContract.OtherPartition.PartitionKey,
                new[] { (ScalarIndexCrashContract.ValueAlpha, Array.Empty<string>()), (ScalarIndexCrashContract.ValueBeta, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueGamma, Array.Empty<string>()), (ScalarIndexCrashContract.ValueDelta,
                        new[] { ScalarIndexCrashContract.OtherPartitionId }), (ScalarIndexCrashContract.ValueEpsilon, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueRollback, Array.Empty<string>()), (ScalarIndexCrashContract.ValueZeta, Array.Empty<string>()),
                    (ScalarIndexCrashContract.ValueTheta, Array.Empty<string>()), (ScalarIndexCrashContract.ValueHealthy, Array.Empty<string>()) })));
    }

    private static ScalarIndexCrashSnapshot Create(ScalarIndexDocumentState[] documents,
        ScalarIndexMembership[] memberships) => new(documents, memberships);

    private static ScalarIndexDocumentState Document(string partitionKey, string id, string json, long revision,
        bool deleted = false) => new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
            ScalarIndexCrashContract.Partition.TransactionDomainId, partitionKey, ScalarIndexCrashContract.Collection,
            id, json, revision, deleted);

    private static ScalarIndexDocumentState Missing(string partitionKey, string id)
        => new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
            ScalarIndexCrashContract.Partition.TransactionDomainId, partitionKey, ScalarIndexCrashContract.Collection,
            id, null, MissingRevision, false);

    private static ScalarIndexMembership[] Memberships(params (string PartitionKey, (string Value, string[] Ids)[] Values)[] partitions)
        => [.. partitions.SelectMany(partition => partition.Values.Select(item =>
            new ScalarIndexMembership(partition.PartitionKey, item.Value, item.Ids)))];
}

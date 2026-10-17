using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class PackedAnnStressColdRestore
{
    private const string FileName = "managed-ann-stress-native.bin";

    internal static async Task VerifyAsync(TestDatabase database, PackedAnnIndex index, VectorRecord[] records,
        float[] query, DistanceMetric metric, CancellationToken token)
    {
        var path = Path.Combine(database.Directory, FileName);
        var storage = Options.Create(new PackedAnnStorageOptions());
        byte[] digest;
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
        {
            digest = index.Save(output, storage, PackedAnnIndexTestSupport.Budget(database, token: token));
            await output.FlushAsync(token);
            RandomAccess.FlushToDisk(output.SafeFileHandle);
        }
        var canonical = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var identity = database.Store.Identity;
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        await Assert.That(NativeSerialization.Serialize(reopened.Identity)
            .SequenceEqual(NativeSerialization.Serialize(identity))).IsTrue();
        await PackedAnnStressOracle.UnchangedAsync(reopened, canonical, position);
        var owner = QueueWholeFlowStorage.Open(reopened);
        var restoredRecords = owner.WithVectors(PackedAnnTestData.Principal, database.Partition,
            PackedAnnTestData.Collection, PackedAnnTestData.Field(metric), (_, _, pairs) =>
                pairs.Select(pair => pair.Vector).OrderBy(row => row.DocumentId, StringComparer.Ordinal).ToArray());
        await Assert.That(NativeSerialization.Serialize(restoredRecords))
            .IsEquivalentTo(NativeSerialization.Serialize(records), CollectionOrdering.Matching);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var budget = new AnnWorkBudget(new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(owner.Limits),
            cancellationToken: token), PackedAnnIndexTestSupport.GenerousWorkLimit);
        var restored = PackedAnnIndex.Load(input, digest, UnitExecutionOptions.PackedAnn(PackedAnnDeleteReinsertStress.Policy),
            storage, budget);
        var result = restored.Search(query, restoredRecords.Length, null, budget);
        await PackedAnnStressOracle.ExactAsync(result, restoredRecords, query, metric);
        await PackedAnnStressOracle.UnchangedAsync(reopened, canonical, position);
    }
}

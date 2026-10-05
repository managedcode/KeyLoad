using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadOnlyCollectionValidationTests
{
    private const string NullKeyJson = "{\"key\":null,\"value\":\"BAUG\"}";
    private const string NullValueJson = "{\"key\":\"AQID\",\"value\":null}";
    private const string MissingKeyJson = "{\"value\":\"BAUG\"}";
    private const string MissingValueJson = "{\"key\":\"AQID\"}";
    private const string NullSigningKeyJson = "{\"formatVersion\":1,\"keyCodecVersion\":2,\"nodeId\":\"00000000-0000-0000-0000-000000000001\",\"incarnation\":\"00000000-0000-0000-0000-000000000002\",\"signingKey\":null,\"durability\":\"ProcessDurable\"}";
    private const string NullVectorJson = "{\"documentId\":\"doc\",\"field\":\"embedding\",\"space\":{\"id\":\"space\",\"dimension\":3,\"metric\":\"DotProduct\",\"model\":\"m\",\"version\":\"1\"},\"values\":null,\"documentRevision\":4}";
    private const string MissingVectorJson = "{\"documentId\":\"doc\",\"field\":\"embedding\",\"space\":{\"id\":\"space\",\"dimension\":3,\"metric\":\"DotProduct\",\"model\":\"m\",\"version\":\"1\"},\"documentRevision\":4}";
    private const string NullIndexesJson = "{\"name\":\"docs\",\"kind\":\"Collection\",\"transactionDomainId\":\"domain\",\"indexes\":null}";
    private const string MissingIndexesJson = "{\"name\":\"docs\",\"kind\":\"Collection\",\"transactionDomainId\":\"domain\"}";
    private const string PartitionKey = "p";
    private const string MalformedBase64Json = "{\"key\":\"not-base64!\",\"value\":\"BAUG\"}";
    private const string WrongByteKindJson = "{\"key\":[1,2,3],\"value\":\"BAUG\"}";
    private const string WrongVectorKindJson = "{\"documentId\":\"doc\",\"field\":\"embedding\",\"space\":{\"id\":\"space\",\"dimension\":3,\"metric\":\"DotProduct\",\"model\":\"m\",\"version\":\"1\"},\"values\":\"1,2,3\",\"documentRevision\":4}";

    [Test]
    public async Task AcRoc002RequiredByteBuffersRejectExplicitNullAndMissingMembers()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(NullKeyJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(NullValueJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(MissingKeyJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(MissingValueJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<StorageMutation>(Bytes(NullKeyJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<StorageMutation>(Bytes(MissingKeyJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<StorageMutation>(Bytes(MissingValueJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<StoreIdentity>(Bytes(NullSigningKeyJson)));

        var tombstone = JsonDefaults.Deserialize<StorageMutation>(Bytes(NullValueJson));
        await Assert.That(JsonDefaults.Serialize(tombstone)).IsEquivalentTo(Bytes(NullValueJson),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcRoc002RequiredVectorAndPolicyCollectionsRejectNullOrDefault()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<VectorRecord>(Bytes(NullVectorJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<VectorRecord>(Bytes(MissingVectorJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<ResourceDefinition>(Bytes(NullIndexesJson)));
        var resource = JsonDefaults.Deserialize<ResourceDefinition>(Bytes(MissingIndexesJson));

        await Assert.That(resource.Indexes.Length).IsEqualTo(0);
    }

    [Test]
    public void AcRoc002DefaultImmutableArraysAndMalformedKindsRejectBeforePersistence()
    {
        var space = new VectorSpace("space", 3, DistanceMetric.DotProduct, "m", "1");
        var requiredDefault = new VectorRecord("doc", "embedding", space, default(ImmutableArray<float>), 4);
        var optionalDefault = new SearchRequest(new("t", "d", "domain", PartitionKey), "docs",
            Vector: default(ImmutableArray<float>));

        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Serialize(requiredDefault));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Serialize(optionalDefault));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(MalformedBase64Json)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(Bytes(WrongByteKindJson)));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<VectorRecord>(Bytes(WrongVectorKindJson)));
    }

    [Test]
    public async Task AcRoc003And004OwnedScanAndPointResultsStayIndependentOfCallerAndStoreBuffers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-readonly-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var key = Bytes("p/one");
            var value = Bytes("first");
            store.Commit((tx, _) => { tx.Put(key, value); return true; });
            key[0] = (byte)'x';
            value[0] = (byte)'x';
            var originalKey = Bytes("p/one");
            var page = store.Read(view => view.Scan(Bytes("p/"), 10));
            var pageKey = page.Records[0].Key;
            var pageValue = page.Records[0].Value;
            store.Commit((tx, _) => { tx.Put(originalKey, Bytes("second")); return true; });
            var owned = store.Read(view => view.ReadOwnedValue(originalKey))!;
            owned[0] = (byte)'x';

            await Assert.That(pageKey.ToArray()).IsEquivalentTo(originalKey,
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(pageValue.ToArray()).IsEquivalentTo(Bytes("first"),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(store.Read(view => view.ReadOwnedValue(originalKey))).IsEquivalentTo(Bytes("second"),
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}

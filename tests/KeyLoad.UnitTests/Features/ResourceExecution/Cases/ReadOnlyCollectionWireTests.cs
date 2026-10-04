using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadOnlyCollectionWireTests
{
    private const string RecordJson = "{\"key\":\"AQID\",\"value\":\"BAUG\"}";
    private const string RecordFingerprint = "556d50c0d1e0cf4d1fc541fa67bc32e250a61df29b0f71a470d2d15b211e5496";
    private const string VectorJson = "{\"documentId\":\"doc\",\"field\":\"embedding\",\"space\":{\"id\":\"space\",\"dimension\":3,\"metric\":\"DotProduct\",\"model\":\"m\",\"version\":\"1\"},\"values\":[1,2.5,-3],\"documentRevision\":4}";
    private const string OrderedProjectionJson = "{\"indexGeneration\":7,\"resources\":[\"z\",\"a\"],\"mutationKinds\":[\"putDocument\",\"deleteDocument\"]}";
    private const string PartitionKey = "p";
    private const string VectorKey = "vector";
    private const string IndexesKey = "indexes";
    private const string FieldPoliciesKey = "fieldPolicies";
    private const string HeaderPoliciesKey = "headerPolicies";
    private const string MutationsKey = "mutations";
    private const string KindKey = "kind";

    [Test]
    public async Task AcRoc002FloatVectorAndOrderedCollectionsKeepExactJsonArrays()
    {
        var space = new VectorSpace("space", 3, DistanceMetric.DotProduct, "m", "1");
        var vector = new VectorRecord("doc", "embedding", space, [1f, 2.5f, -3f], 4);
        var ordered = new ProjectionConsumerDefinition(7, ["z", "a"], ["putDocument", "deleteDocument"]);

        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(vector))).IsEqualTo(VectorJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(ordered))).IsEqualTo(OrderedProjectionJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<VectorRecord>(Encoding.UTF8.GetBytes(VectorJson)))))
            .IsEqualTo(VectorJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<ProjectionConsumerDefinition>(Encoding.UTF8.GetBytes(OrderedProjectionJson)))))
            .IsEqualTo(OrderedProjectionJson);
    }

    [Test]
    public async Task AcRoc002NullableVectorAndInitializedCollectionsKeepNullEmptyAndMissingDistinct()
    {
        var partition = new PartitionRef("t", "d", "domain", PartitionKey);
        var missing = new SearchRequest(partition, "docs");
        var empty = new SearchRequest(partition, "docs", Vector: []);
        var resource = new ResourceDefinition("docs", ResourceKind.Collection, "domain");
        using var missingJson = JsonDocument.Parse(JsonDefaults.Serialize(missing));
        using var emptyJson = JsonDocument.Parse(JsonDefaults.Serialize(empty));
        using var resourceJson = JsonDocument.Parse(JsonDefaults.Serialize(resource));

        await Assert.That(missingJson.RootElement.GetProperty(VectorKey).ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(emptyJson.RootElement.GetProperty(VectorKey).ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(emptyJson.RootElement.GetProperty(VectorKey).GetArrayLength()).IsEqualTo(0);
        await Assert.That(resourceJson.RootElement.GetProperty(IndexesKey).GetArrayLength()).IsEqualTo(0);
        await Assert.That(resourceJson.RootElement.GetProperty(FieldPoliciesKey).GetArrayLength()).IsEqualTo(0);
        await Assert.That(resourceJson.RootElement.GetProperty(HeaderPoliciesKey).GetArrayLength()).IsEqualTo(0);
        await Assert.That(JsonDefaults.Deserialize<SearchRequest>(JsonDefaults.Serialize(empty)).Vector is null).IsFalse();
        await Assert.That(JsonDefaults.Deserialize<SearchRequest>(JsonDefaults.Serialize(missing)).Vector is null).IsTrue();
    }

    [Test]
    public async Task AcRoc002PolymorphicMutationSequencePreservesOrderAndDiscriminators()
    {
        var partition = new PartitionRef("t", "d", "domain", PartitionKey);
        var command = new CommandRequest(Guid.Parse("00000000-0000-0000-0000-000000000003"), partition,
            [new DeleteDocument("docs", "first", 2), new PutDocument("docs", "second", "{}", 0)]);
        var bytes = JsonDefaults.Serialize(command);
        using var document = JsonDocument.Parse(bytes);
        var mutations = document.RootElement.GetProperty(MutationsKey);
        var roundTrip = JsonDefaults.Deserialize<CommandRequest>(bytes);

        await Assert.That(mutations.GetArrayLength()).IsEqualTo(2);
        await Assert.That(mutations[0].GetProperty(KindKey).GetString()).IsEqualTo("deleteDocument");
        await Assert.That(mutations[1].GetProperty(KindKey).GetString()).IsEqualTo("putDocument");
        await Assert.That(roundTrip.Mutations[0]).IsTypeOf<DeleteDocument>();
        await Assert.That(roundTrip.Mutations[1]).IsTypeOf<PutDocument>();
        await Assert.That(JsonDefaults.Serialize(roundTrip)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcRoc002CanonicalFingerprintMatchesIndependentLegacyBytes()
    {
        var record = JsonDefaults.Deserialize<KeyValueRecord>(Encoding.UTF8.GetBytes(RecordJson));
        await Assert.That(JsonData.Fingerprint(record)).IsEqualTo(RecordFingerprint);
    }
}

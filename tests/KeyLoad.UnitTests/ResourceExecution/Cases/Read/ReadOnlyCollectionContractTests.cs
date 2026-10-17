using System.Reflection;
using System.Text;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadOnlyCollectionContractTests
{
    private const string OwnedReadName = "ReadOwnedValue";
    private const string FormerReadName = "Get";
    private const string RecordJson = "{\"key\":\"AQID\",\"value\":\"BAUG\"}";
    private const string TombstoneJson = "{\"key\":\"AQID\",\"value\":null}";
    private const string EmptyValueJson = "{\"key\":\"AQID\",\"value\":\"\"}";
    private const string IdentityJson = "{\"formatVersion\":1,\"keyCodecVersion\":2,\"nodeId\":\"00000000-0000-0000-0000-000000000001\",\"incarnation\":\"00000000-0000-0000-0000-000000000002\",\"signingKey\":\"AQID\",\"durability\":\"ProcessDurable\",\"dispatchPaused\":false,\"readGeneration\":0}";
    private const string JournalIdentityJson = "{\"formatVersion\":1,\"keyCodecVersion\":2,\"nodeId\":\"00000000-0000-0000-0000-000000000001\",\"incarnation\":\"00000000-0000-0000-0000-000000000002\",\"signingKey\":\"AQID\",\"durability\":\"ProcessDurable\",\"dispatchPaused\":false,\"readGeneration\":0,\"minimumReaderContract\":1}";

    [Test]
    public async Task AcRoc001ExportedAbstractionsPropertiesNeverExposeArrays()
    {
        var offenders = typeof(CommandRequest).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(property => property.DeclaringType?.Assembly == typeof(CommandRequest).Assembly && property.PropertyType.IsArray)
            .Select(property => property.DeclaringType!.FullName + "." + property.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task AcRoc004OwnedPointMethodHasOneAccurateNameAndSignature()
    {
        var owned = typeof(IKeyValueView).GetMethod(OwnedReadName);
        await Assert.That(owned).IsNotNull();
        await Assert.That(owned!.ReturnType).IsEqualTo(typeof(byte[]));
        await Assert.That(owned.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(byte[]) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(typeof(IKeyValueView).GetMethod(FormerReadName)).IsNull();
    }

    [Test]
    public async Task AcRoc002ByteBuffersKeepExactLegacyBase64AndNullableTombstoneWire()
    {
        byte[] key = [1, 2, 3];
        byte[] value = [4, 5, 6];
        var record = new KeyValueRecord(key, value);
        var tombstone = new StorageMutation(key, null);
        var empty = new StorageMutation(key, Array.Empty<byte>());
        var identity = new StoreIdentity(1, 2,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            key, DurabilityProfile.ProcessDurable);

        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(record))).IsEqualTo(RecordJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(tombstone))).IsEqualTo(TombstoneJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(empty))).IsEqualTo(EmptyValueJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(identity))).IsEqualTo(IdentityJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<KeyValueRecord>(Encoding.UTF8.GetBytes(RecordJson)))))
            .IsEqualTo(RecordJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<StorageMutation>(Encoding.UTF8.GetBytes(TombstoneJson)))))
            .IsEqualTo(TombstoneJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<StorageMutation>(Encoding.UTF8.GetBytes(EmptyValueJson)))))
            .IsEqualTo(EmptyValueJson);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(JsonDefaults.Deserialize<StoreIdentity>(Encoding.UTF8.GetBytes(IdentityJson)))))
            .IsEqualTo(IdentityJson);
    }

    [Test]
    public async Task AcRoc002NativeJournalReaderFenceIsRetainedInPublicIdentityJson()
    {
        var identity = new StoreIdentity(1, 2,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            new byte[] { 1, 2, 3 }, DurabilityProfile.ProcessDurable,
            MinimumReaderContract: 1);

        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(identity))).IsEqualTo(JournalIdentityJson);
        var restored = JsonDefaults.Deserialize<StoreIdentity>(Encoding.UTF8.GetBytes(JournalIdentityJson));
        await Assert.That(restored.MinimumReaderContract).IsEqualTo(1);
        await Assert.That(Encoding.UTF8.GetString(JsonDefaults.Serialize(restored))).IsEqualTo(JournalIdentityJson);
        await Assert.That(JsonDefaults.Deserialize<StoreIdentity>(Encoding.UTF8.GetBytes(IdentityJson)).MinimumReaderContract)
            .IsEqualTo(0);
    }
}

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobIntegrityGoldenTests
{
    private const string Incarnation = "00112233-4455-6677-8899-aabbccddeeff";
    private const string UploadId = "ffeeddcc-bbaa-9988-7766-554433221100";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string Resource = "blobs";
    private const string BlobId = "asset-1";
    private const string FirstPartitionKey = "partition-a";
    private const long DeclaredLength = 70_000;
    private const long RawPartBytes = 65_536;
    private const string ExpectedEncodedKey = "0150626c6f622d636861696e2d763100005030303131323233333434353536363737383839396161626263636464656566660000"
        + "5074656e616e7400005064617461626173650000506f7264657273000050706172746974696f6e2d61000050626c6f62730000"
        + "5061737365742d310000506666656564646363626261613939383837373636353534343333323231313030000030800000000001"
        + "1170308000000000010000";
    private const string ExpectedInitialHash = "0b452da9f0a71b219675dc8b153f8b51ec5e827c8665ae2c3362eab673650462";
    private const string PartText = "sample-part";
    private const string ExpectedPartHash = "eb7861c49e25caf6ff9e32bac3b26c8ac49e00247a2c05363682eb5f1d32221e";
    private const string ExpectedNextHash = "229317a51ec51b5824366dd0db8d35d3a184783567317b41d1604554dc22ca55";

    [Test]
    public async Task AcBlob007IntegrityGoldenVectorBindsScopeLayoutAndOrderedParts()
    {
        var blob = new BlobRef(new(Tenant, Database, Domain, FirstPartitionKey), Resource, BlobId);
        var initial = BlobIntegrity.InitialHash(Guid.Parse(Incarnation), blob, Guid.Parse(UploadId), DeclaredLength);
        var part = BlobIntegrity.PartHash(System.Text.Encoding.UTF8.GetBytes(PartText));
        var next = BlobIntegrity.NextHash(initial, 0, PartText.Length, part);
        var otherScope = blob with { Resource = "other-resource" };
        var otherPartition = blob with { Partition = blob.Partition with { PartitionKey = "partition-b" } };
        var expectedKey = KeyLoad.Storage.KeyCodec.Encode("blob-chain-v1", Incarnation.Replace("-", string.Empty, StringComparison.Ordinal),
            Tenant, Database, Domain, FirstPartitionKey, Resource, BlobId,
            UploadId.Replace("-", string.Empty, StringComparison.Ordinal), DeclaredLength, RawPartBytes);

        await Assert.That(BlobIntegrity.IntegrityAlgorithm).IsEqualTo("sha256-chain-v1");
        await Assert.That(Convert.ToHexStringLower(expectedKey)).IsEqualTo(ExpectedEncodedKey);
        await Assert.That(initial).IsEqualTo(ExpectedInitialHash);
        await Assert.That(part).IsEqualTo(ExpectedPartHash);
        await Assert.That(next).IsEqualTo(ExpectedNextHash);
        await Assert.That(BlobIntegrity.InitialHash(Guid.Parse(Incarnation), otherScope,
            Guid.Parse(UploadId), DeclaredLength)).IsNotEqualTo(initial);
        await Assert.That(BlobIntegrity.InitialHash(Guid.Parse(Incarnation), otherPartition,
            Guid.Parse(UploadId), DeclaredLength)).IsNotEqualTo(initial);
        await Assert.That(BlobIntegrity.InitialHash(Guid.Parse(Incarnation), blob,
            Guid.Parse(UploadId), DeclaredLength + 1)).IsNotEqualTo(initial);
        await Assert.That(BlobIntegrity.NextHash(initial, 1, PartText.Length, part)).IsNotEqualTo(next);
    }
}

using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-BLOB-002/003/006: capability dispatch uses genuine node-local storage with current principal authority.</summary>
internal sealed class BlobGrainRoutingReadTests
{
    private const string MissingPrincipal = "missing-principal";
    private const int MetadataKind = 20;
    private const int UploadInfoKind = 21;
    private const int RangeKind = 22;
    private const int ListKind = 23;
    private const int ExistingStatusKind = 19;

    /// <summary>Append-only capability values preserve the existing routing wire contract.</summary>
    [Test]
    public async Task AcBlob007BlobReadKindsAreAppendOnlyAndTheHelperHandlesExactlyFour()
    {
        await Assert.That(Numeric(GrainReadKind.NodeStatus)).IsEqualTo(ExistingStatusKind);
        await Assert.That(Numeric(GrainReadKind.BlobMetadata)).IsEqualTo(MetadataKind);
        await Assert.That(Numeric(GrainReadKind.BlobUploadInfo)).IsEqualTo(UploadInfoKind);
        await Assert.That(Numeric(GrainReadKind.BlobRange)).IsEqualTo(RangeKind);
        await Assert.That(Numeric(GrainReadKind.BlobList)).IsEqualTo(ListKind);
        foreach (var kind in Enum.GetValues<GrainReadKind>())
        {
            await Assert.That(GrainBlobReadCapabilities.Handles(kind)).IsEqualTo(kind is GrainReadKind.BlobMetadata
                or GrainReadKind.BlobUploadInfo or GrainReadKind.BlobRange or GrainReadKind.BlobList);
        }
    }

    private static int Numeric(GrainReadKind value) => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Absent authorized reads return their exact public null/page/NotFound semantics from native storage.</summary>
    [Test]
    public async Task AcBlob006NativeCapabilityDispatchPreservesAbsentObjectAndListResults()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(BlobAgentCases.Resource, ResourceKind.BlobStore);
        var helper = new GrainBlobReadCapabilities(fixture.Database);
        var cases = BlobAgentCases.All();
        foreach (var item in cases.Where(item => item.Name is BlobAgentCases.Metadata or BlobAgentCases.UploadInfo))
        {
            await Assert.That(helper.Execute(item.ReadKind!.Value, BlobAgentCases.Principal, item.Payload,
                TestContext.Current!.Execution.CancellationToken)).IsNull();
        }
        var list = cases.Single(item => item.Name == BlobAgentCases.List);
        var result = (BlobListPage)helper.Execute(list.ReadKind!.Value, BlobAgentCases.Principal, list.Payload,
            TestContext.Current!.Execution.CancellationToken)!;
        await Assert.That(result.Items.IsEmpty).IsTrue();
        await Assert.That(result.NextAfterId).IsNull();
        var range = cases.Single(item => item.Name == BlobAgentCases.Range);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => helper.Execute(range.ReadKind!.Value,
            BlobAgentCases.Principal, range.Payload, TestContext.Current!.Execution.CancellationToken)).Code).IsEqualTo(ErrorCode.NotFound);
    }

    /// <summary>Cancellation stops every read, missing persisted authority is denied, and unrelated capabilities are unsupported.</summary>
    [Test]
    public async Task AcBlob003ReadDispatchChecksCancellationAndPersistedAuthorityBeforeReturningData()
    {
        using var fixture = new TestDatabase();
        fixture.Configure(BlobAgentCases.Resource, ResourceKind.BlobStore);
        var helper = new GrainBlobReadCapabilities(fixture.Database);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        foreach (var item in BlobAgentCases.All().Where(item => item.ReadKind.HasValue))
        {
            await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() => helper.Execute(item.ReadKind!.Value,
                BlobAgentCases.Principal, item.Payload, cancellation.Token))).IsNotNull();
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => helper.Execute(item.ReadKind!.Value,
                MissingPrincipal, item.Payload, TestContext.Current!.Execution.CancellationToken)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => helper.Execute(GrainReadKind.Document,
            BlobAgentCases.Principal, BlobAgentCases.All()[0].Payload, TestContext.Current!.Execution.CancellationToken)).Code)
            .IsEqualTo(ErrorCode.UnsupportedCapability);
    }
}

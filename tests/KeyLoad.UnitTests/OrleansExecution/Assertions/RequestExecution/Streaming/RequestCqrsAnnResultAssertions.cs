using System.Security.Cryptography;
using KeyLoad.Query;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestCqrsAnnResultAssertions
{
    internal static AnnManifest Manifest(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request)
    {
        var root = Path.Combine(fixture.Database.Directory, AnnProtocol.RootDirectory);
        var owner = AnnReceiptFiles.Read<AnnRootReceipt>(Path.Combine(root,
            AnnProtocol.RootReceipt), new AnnExecutionOptions()).Value;
        var selected = owner.Published.Single(row => row.Key.Consumer == request.Consumer
            && row.Key.IndexGeneration == request.IndexGeneration);
        return AnnReceiptFiles.Read<AnnManifest>(Path.Combine(root,
            selected.Pointer.GenerationLeaf, AnnProtocol.ManifestFile), new AnnExecutionOptions()).Value;
    }

    internal static async Task RequireAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        AnnMaintenanceResult result, long before, bool changed, CancellationToken token)
    {
        var manifest = Manifest(fixture, request);
        var source = result.Source ?? throw new InvalidOperationException();
        var checkpoint = result.Checkpoint ?? throw new InvalidOperationException();
        await RequestCqrsAnnAssertions.EqualAsync(new AnnMaintenanceResult(request.CommandId, request.Consumer,
            request.IndexGeneration, AnnMaintenancePhase.Completed, manifest.Source,
            changed ? RequestCqrsAnnTestData.ChangedCount : RequestCqrsAnnTestData.OriginalCount,
            Convert.ToHexStringLower(manifest.IndexSha256), checkpoint), result);
        await SourceAsync(fixture, request, source, before, changed);
        await Assert.That(checkpoint.Checkpoint).IsEqualTo(source.ThroughSequence);
        await Assert.That(checkpoint.Receipt.Mutations.IsEmpty).IsTrue();
        await Assert.That(checkpoint.Receipt.CommandId).IsEqualTo(manifest.CheckpointIntent!.CommandId);
        var index = Path.Combine(fixture.Database.Directory, AnnProtocol.RootDirectory,
            manifest.GenerationLeaf, AnnProtocol.IndexFile);
        await using (var file = File.OpenRead(index))
        {
            await Assert.That(Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token))).IsEqualTo(result.IndexSha256);
        }
        await SearchAsync(fixture, request, changed, token);
    }

    private static async Task SourceAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        AnnSourceCut source, long before, bool changed)
    {
        await Assert.That(source.NodeId).IsEqualTo(fixture.Database.Store.Identity.NodeId);
        await Assert.That(source.Incarnation).IsEqualTo(fixture.Database.Store.Identity.Incarnation);
        await Assert.That(source.ReadGeneration).IsEqualTo(fixture.Database.Store.Identity.ReadGeneration);
        await Assert.That(source.StoreFormatVersion).IsEqualTo(fixture.Database.Store.Identity.FormatVersion);
        await Assert.That(source.KeyCodecVersion).IsEqualTo(fixture.Database.Store.Identity.KeyCodecVersion);
        await Assert.That(source.Position).IsGreaterThanOrEqualTo(before);
        await Assert.That(source.Position).IsLessThanOrEqualTo(fixture.Database.Store.Position);
        await Assert.That(source.AppliedPosition).IsLessThanOrEqualTo(fixture.Database.ReadCanonicalCut().AppliedPosition);
        await Assert.That(source.ThroughSequence).IsEqualTo(changed ? RequestCqrsAnnTestData.ChangedTail : RequestCqrsAnnTestData.OriginalTail);
        await Assert.That(source.CorpusSha256).IsEqualTo(AnnSeedDigestOracle.Compute(request.Field,
            request.Space, RequestCqrsAnnTestData.Records(changed)));
    }

    internal static async Task SearchAsync(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        bool changed, CancellationToken token)
    {
        var execution = fixture.RuntimeAnnMaintenance.Value ?? throw new InvalidOperationException();
        var search = new SearchEngine(fixture.Database.Database,
            Options.Create(new QueryExecutionOptions { EnableApproximateSearch = true }), null, execution);
        var image = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var cut = fixture.Database.Store.Position;
        var actual = await search.ApproximateSearchAsync(RequestCqrsAnnTestData.Principal,
            new(RequestCqrsAnnTestData.Version, new(fixture.Database.Partition, request.Collection,
                VectorField: request.Field, Vector: [.. RequestCqrsAnnTestData.Query], Space: request.Space,
                Limit: RequestCqrsAnnTestData.OriginalCount), request.Consumer, request.IndexGeneration), token);
        RankedDocument[] rows = changed
            ? [Row(fixture, request, RequestCqrsAnnTestData.First, RequestCqrsAnnTestData.ChangedJson,
                RequestCqrsAnnTestData.ChangedRevision, RequestCqrsAnnTestData.FirstScore)]
            : [Row(fixture, request, RequestCqrsAnnTestData.First, RequestCqrsAnnTestData.FirstJson,
                RequestCqrsAnnTestData.OriginalRevision, RequestCqrsAnnTestData.FirstScore),
               Row(fixture, request, RequestCqrsAnnTestData.Second, RequestCqrsAnnTestData.FirstJson,
                RequestCqrsAnnTestData.OriginalRevision, RequestCqrsAnnTestData.SecondScore)];
        await RequestCqrsAnnAssertions.EqualAsync(new AnnSearchPage(RequestCqrsAnnTestData.Version,
            [.. rows], cut, AnnPageMode.Exact, true, request.IndexGeneration, AnnPageMode.Approximate), actual);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store).AsSpan().SequenceEqual(image)).IsTrue();
    }

    private static RankedDocument Row(RequestCqrsHostedRuntimeFixture fixture, AnnMaintenanceRequest request,
        string id, string json, long revision, double score)
        => new(new(fixture.Database.Reference(RequestCqrsAnnTestData.CollectionCreation, request.Collection, id),
            revision, json, false, []), score);
}

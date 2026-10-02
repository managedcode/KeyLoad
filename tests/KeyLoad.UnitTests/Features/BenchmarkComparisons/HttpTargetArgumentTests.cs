using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class HttpTargetArgumentTests
{
    private const string DatasetParameter = "dataset";
    private const string ApiKey = "test-key";
    private const string RunId = "cb4c5e4f-a2c3-4da1-b557-107109d85c68";
    private const string KeyLoadImage = "keyload-test-image";
    private const string Neo4jImage = "neo4j:5.26.0";
    private const string QdrantImage = "qdrant/qdrant:v1.16.0";
    private const string KurrentImage = "kurrentplatform/kurrentdb:26.1.2";
    private const string OpenSearchImage = "opensearchproject/opensearch:3.2.0";
    private const string KurrentConnection = "kurrentdb://localhost:2113?tls=true";
    private static readonly Uri AbsoluteProbe = new("http://localhost/");

    [Test]
    public async Task AcMp010HttpTargetsRejectMissingDatasetBeforeAnyHttpWork()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var keyLoadHttp = new HttpClient();
        using var neo4jHttp = new HttpClient();
        using var qdrantHttp = new HttpClient();
        using var kurrentHttp = new HttpClient();
        using var openSearchHttp = new HttpClient();
        var keyLoad = new KeyLoadTarget(keyLoadHttp, ApiKey, RunId, KeyLoadImage);
        var neo4j = new Neo4jTarget(neo4jHttp, RunId, Neo4jImage);
        var qdrant = new QdrantTarget(qdrantHttp, RunId, QdrantImage);
        var kurrent = new KurrentTarget(KurrentConnection, [kurrentHttp], RunId, KurrentImage, ComparisonTopology.Standalone);
        var openSearch = new OpenSearchTarget(openSearchHttp, RunId, OpenSearchImage, ComparisonTopology.Standalone);

        await AssertMissingDatasetAsync(keyLoad, cancellationToken);
        await AssertMissingDatasetAsync(neo4j, cancellationToken);
        await AssertMissingDatasetAsync(qdrant, cancellationToken);
        await AssertMissingDatasetAsync(kurrent, cancellationToken);
        await AssertMissingDatasetAsync(openSearch, cancellationToken);

        await keyLoad.DisposeAsync();
        await neo4j.DisposeAsync();
        await qdrant.DisposeAsync();
        await kurrent.DisposeAsync();
        await openSearch.DisposeAsync();
    }

    [Test]
    public async Task AcMp012NeverInitializedHttpTargetsDisposeClientsWithoutRemoteCleanup()
    {
        using var keyLoadHttp = new HttpClient();
        using var keyLoadPeer = new HttpClient();
        using var neo4jHttp = new HttpClient();
        using var qdrantHttp = new HttpClient();
        using var qdrantPeer = new HttpClient();
        using var kurrentHttp = new HttpClient();
        using var kurrentPeer = new HttpClient();
        using var openSearchHttp = new HttpClient();
        var targets = new IComparisonTarget[]
        {
            new KeyLoadTarget(keyLoadHttp, ApiKey, RunId, KeyLoadImage, [keyLoadPeer]),
            new Neo4jTarget(neo4jHttp, RunId, Neo4jImage),
            new QdrantTarget(qdrantHttp, RunId, QdrantImage, nodeClients: [qdrantPeer]),
            new KurrentTarget(KurrentConnection, [kurrentHttp, kurrentPeer], RunId, KurrentImage, ComparisonTopology.Standalone),
            new OpenSearchTarget(openSearchHttp, RunId, OpenSearchImage, ComparisonTopology.Standalone)
        };

        foreach (var target in targets)
        {
            await target.DisposeAsync();
        }

        await AssertDisposedAsync(keyLoadHttp);
        await AssertDisposedAsync(keyLoadPeer);
        await AssertDisposedAsync(neo4jHttp);
        await AssertDisposedAsync(qdrantHttp);
        await AssertDisposedAsync(qdrantPeer);
        await AssertDisposedAsync(kurrentHttp);
        await AssertDisposedAsync(kurrentPeer);
        await AssertDisposedAsync(openSearchHttp);
    }

    private static async Task AssertMissingDatasetAsync(IComparisonTarget target, CancellationToken cancellationToken)
    {
        var error = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => target.InitializeAsync(null!, cancellationToken));
        await Assert.That(error!.ParamName).IsEqualTo(DatasetParameter);
    }

    private static async Task AssertDisposedAsync(HttpClient client)
    {
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.GetAsync(AbsoluteProbe));
    }
}

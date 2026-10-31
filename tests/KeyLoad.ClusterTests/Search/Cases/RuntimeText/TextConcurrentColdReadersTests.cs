using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Search;

/// <summary>Independent SDK callers build and read their authorized cold text scopes without saturation.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "execution-text-concurrent-cold")]
[NotInParallel]
internal sealed class TextConcurrentColdReadersTests(ClusterFixture fixture)
{
    private const int Callers = 8;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ParallelColdSdkReadersRetainTheirOwnAuthorityAndExactRanks(bool distinctPrincipals)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await TextClusterScenario.CreateAsync(fixture, deadline.Token);
        var identities = new List<McpPersistedIdentity>();
        for (var index = 0; index < (distinctPrincipals ? Callers : 1); index++)
        {
            identities.Add(await scenario.CreateReaderAsync(fixture, true, false, false, false, deadline.Token));
        }
        var requests = Enumerable.Range(0, Callers).Select(async index =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            http.DefaultRequestVersion = System.Net.HttpVersion.Version20;
            http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            var sdk = new KeyLoadClient(http, identities[index % identities.Count].Secret,
                IntegrationClientOptions.Execution());
            return await sdk.SearchAsync(scenario.Text("needle"), deadline.Token);
        }).ToArray();
        var results = await Task.WhenAll(requests);
        foreach (var response in results)
        {
            var rows = await McpCallerAssertions.SdkSuccessAsync(response);
            await Assert.That(rows.Length).IsEqualTo(2);
            await Assert.That(rows[0].Document.Reference).IsEqualTo(scenario.StoredReference(TextClusterScenario.FirstId));
            await Assert.That(rows[1].Document.Reference).IsEqualTo(scenario.StoredReference(TextClusterScenario.SecondId));
            await Assert.That(rows[0].Score).IsEqualTo(1d / 61d);
            await Assert.That(rows[1].Score).IsEqualTo(1d / 62d);
            await Assert.That(rows.All(row => !row.Document.Json.Contains(TextClusterScenario.Secret,
                StringComparison.Ordinal))).IsTrue();
        }
        using var followingHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var following = new KeyLoadClient(followingHttp, identities[0].Secret, IntegrationClientOptions.Execution());
        var healthy = await McpCallerAssertions.SdkSuccessAsync(await following.SearchAsync(scenario.Text("needle"), deadline.Token));
        await Assert.That(healthy.Select(row => row.Document.Reference.Id).ToArray())
            .IsEquivalentTo(new[] { TextClusterScenario.FirstId, TextClusterScenario.SecondId }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}

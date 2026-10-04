using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "rf3")]
[NotInParallel]
internal sealed class ClusterTests(ClusterFixture fixture)
{
    private const string InvalidCredential = "root.invalid-untrusted-credential-long-enough";
    private const string Node1 = "node1";
    private const string HttpEndpoint = "http";
    private const string InternalCommandsPath = "/internal/commands";
    private const string ReadBarrierPath = "/internal/read-barrier";
    private const string SiloPath = "/internal/silo";

    [Test]
    public async Task ReplicatedAtomicBatchSurvivesLeaderContainerKillAndMinorityRejectsWrites()
        => await LeaderLossScenario.RunAsync(fixture);

    [Test]
    public async Task UnsignedPeerRequestsAndClientSuppliedPrincipalAreRejected()
    {
        using var http = fixture.App.CreateHttpClient(Node1, HttpEndpoint);
        using var legacyCommands = await http.PostAsync(new Uri(InternalCommandsPath, UriKind.Relative), content: null,
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(legacyCommands.StatusCode).IsEqualTo(System.Net.HttpStatusCode.NotFound);
        using var unsignedBarrier = await http.GetAsync(
            new Uri(ReadBarrierPath, UriKind.Relative), TestContext.Current!.Execution.CancellationToken);
        await Assert.That(unsignedBarrier.StatusCode).IsEqualTo(System.Net.HttpStatusCode.NotFound);
        using var unsignedSilo = await http.GetAsync(
            new Uri(SiloPath, UriKind.Relative), TestContext.Current!.Execution.CancellationToken);
        await Assert.That(unsignedSilo.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Unauthorized);
        var invalid = fixture.Client(Node1, InvalidCredential);
        await Assert.That((await invalid.StatusAsync(TestContext.Current!.Execution.CancellationToken)).IsFailed).IsTrue();
    }

    [Test]
    public async Task RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes()
        => await RetainedReplicaSnapshotScenario.RunAsync(fixture);

    [Test]
    public async Task SqlJsonAndCSharpUseTheSameAuthorizedHttpQueryContract()
        => await AuthorizedQueryScenario.RunAsync(fixture);
}

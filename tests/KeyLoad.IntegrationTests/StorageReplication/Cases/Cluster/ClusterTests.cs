using KeyLoad.IntegrationTests.Features.StorageReplication;

namespace KeyLoad.IntegrationTests;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "cluster")]
[NotInParallel]
internal sealed class ClusterTests(ClusterFixture fixture)
{
    private const string InvalidCredential = "root.invalid-untrusted-credential-long-enough";
    private const string Node1 = "node1";

    [Test]
    public async Task AtomicBatchSurvivesSiloRestartAndMissingCopiesRejectWrites()
        => await SiloRestartScenario.RunAsync(fixture);

    [Test]
    public async Task InvalidClientCredentialCannotReadNodeStatus()
    {
        var invalid = fixture.Client(Node1, InvalidCredential);
        await Assert.That((await invalid.StatusAsync(TestContext.Current!.Execution.CancellationToken)).IsFailed).IsTrue();
    }

    [Test]
    public async Task SqlJsonAndCSharpUseTheSameAuthorizedHttpQueryContract()
        => await AuthorizedQueryScenario.RunAsync(fixture);
}

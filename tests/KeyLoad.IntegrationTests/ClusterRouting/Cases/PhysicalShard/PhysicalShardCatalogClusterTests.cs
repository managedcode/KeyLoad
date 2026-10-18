using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "cluster")]
[NotInParallel]
internal sealed class PhysicalShardCatalogClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcScat003AllVotersValidateOneCatalogAndRestartKeepsTheConfiguredIdentity()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsClusterProtocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var configuredIdentity = fixture.PhysicalShardId;
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        await scenario.SeedAsync(fixture, deadline.Token).ConfigureAwait(false);
        await PhysicalShardCatalogClusterAssertions.VerifyAllVotersAsync(fixture, scenario.Reference,
            McpDocumentProtocol.InitialJson, deadline.Token).ConfigureAwait(false);

        await fixture.KillContainerAsync(RequestCqrsClusterProtocol.Node3,
            PhysicalShardCatalogClusterProtocol.RestartScenario, deadline.Token).ConfigureAwait(false);
        await fixture.RestartContainerAsync(RequestCqrsClusterProtocol.Node3, deadline.Token).ConfigureAwait(false);
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(RequestCqrsClusterProtocol.Node3,
            deadline.Token).ConfigureAwait(false);

        await Assert.That(fixture.PhysicalShardId).IsEqualTo(configuredIdentity);
        await PhysicalShardCatalogClusterAssertions.VerifyAllVotersAsync(fixture, scenario.Reference,
            McpDocumentProtocol.InitialJson, deadline.Token).ConfigureAwait(false);
    }
}

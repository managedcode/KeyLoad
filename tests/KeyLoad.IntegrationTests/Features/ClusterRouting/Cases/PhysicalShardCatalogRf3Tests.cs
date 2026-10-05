using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = "rf3")]
[NotInParallel]
internal sealed class PhysicalShardCatalogRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcScat003AllVotersValidateOneCatalogAndRestartKeepsTheConfiguredIdentity()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var configuredIdentity = fixture.PhysicalShardId;
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token).ConfigureAwait(false);
        await scenario.SeedAsync(fixture, deadline.Token).ConfigureAwait(false);
        await PhysicalShardCatalogRf3Assertions.VerifyAllVotersAsync(fixture, scenario.Reference,
            McpDocumentProtocol.InitialJson, deadline.Token).ConfigureAwait(false);

        await fixture.KillContainerAsync(RequestCqrsRf3Protocol.Node3,
            PhysicalShardCatalogRf3Protocol.RestartScenario, deadline.Token).ConfigureAwait(false);
        await fixture.RestartContainerAsync(RequestCqrsRf3Protocol.Node3, deadline.Token).ConfigureAwait(false);
        await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(RequestCqrsRf3Protocol.Node3,
            deadline.Token).ConfigureAwait(false);

        await Assert.That(fixture.PhysicalShardId).IsEqualTo(configuredIdentity);
        await PhysicalShardCatalogRf3Assertions.VerifyAllVotersAsync(fixture, scenario.Reference,
            McpDocumentProtocol.InitialJson, deadline.Token).ConfigureAwait(false);
    }
}

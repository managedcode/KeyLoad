namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class PhysicalShardAuthorityBoundaryRf3Tests
{
    [Test]
    public Task OrderedPlanReloadsPersistedAdministratorBetweenMembersThenRepairsSameOriginalsAndMergesCold()
        => PhysicalShardAuthorityBoundaryRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}

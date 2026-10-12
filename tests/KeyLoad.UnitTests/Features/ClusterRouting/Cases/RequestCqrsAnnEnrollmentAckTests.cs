namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource(nativeAnnMaintenance: true)]
[NotInParallel]
internal sealed class RequestCqrsAnnEnrollmentAckTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public async Task OriginalResultReturnsOnlyAfterCanonicalInitialSettlementAndExactReplayPreservesIt()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await RequestCqrsAnnTestData.SeedAsync(fixture, token);
        var original = seed with { MaintenanceProfile = new(seed.Collection, seed.Field, seed.Space) };
        var before = fixture.Database.Store.Position;
        var built = await RequestCqrsAnnInvocation.RunAsync(fixture, original, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, original, built.Result, before, changed: false, token);
        await RequestCqrsAnnEnrollmentAssertions.RequireAsync(fixture, original);
        var settled = fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
            original.Consumer.Partition).Consumers.Single(value => value.Consumer == original.Consumer);
        await Assert.That(settled.InitialAnnMaintenanceSettled).IsTrue();
        await Assert.That(settled.Checkpoint).IsEqualTo(built.Result.Checkpoint!.Checkpoint);
        var exact = NativeSerialization.Deserialize<AnnMaintenanceRequest>(NativeSerialization.Serialize(original));
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, exact, built.Result, token);
        await RequestCqrsAnnAssertions.EqualAsync(settled,
            fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
                original.Consumer.Partition).Consumers.Single(value => value.Consumer == original.Consumer));
        await RequestCqrsAnnSettlementReplayFlow.RunAsync(fixture, original, built.Result, token);
        var ordinary = original with { CommandId = Guid.NewGuid() };
        var healthy = await RequestCqrsAnnInvocation.RunAsync(fixture, ordinary, token);
        await Assert.That(healthy.Result.CommandId).IsEqualTo(ordinary.CommandId);
        await Assert.That(healthy.Result.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
        await RequestCqrsAnnEnrollmentAssertions.RequireAsync(fixture, original);
        await Assert.That(fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
            original.Consumer.Partition).Consumers.Single(value => value.Consumer == original.Consumer)
            .InitialAnnMaintenanceSettled).IsTrue();
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, ordinary, healthy.Result, token);
    }
}

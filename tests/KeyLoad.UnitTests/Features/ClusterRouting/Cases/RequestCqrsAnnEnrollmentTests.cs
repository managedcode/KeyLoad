namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource(nativeAnnMaintenance: true)]
[NotInParallel]
internal sealed class RequestCqrsAnnEnrollmentTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public async Task NonnullInitialBuildReplaysOriginalAndLaterParentsPreserveImmutableEnrollment()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var seed = await RequestCqrsAnnTestData.SeedAsync(fixture, token);
        var original = seed with
        {
            MaintenanceProfile = new(seed.Collection, seed.Field, seed.Space)
        };
        var before = fixture.Database.Store.Position;
        var built = await RequestCqrsAnnInvocation.RunAsync(fixture, original, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, original, built.Result, before, changed: false, token);
        await RequestCqrsAnnEnrollmentAssertions.RequireAsync(fixture, original);
        var exact = NativeSerialization.Deserialize<AnnMaintenanceRequest>(NativeSerialization.Serialize(original));
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, exact, built.Result, token);
        var fresh = original with
        {
            CommandId = Guid.NewGuid()
        };
        var repeated = await RequestCqrsAnnInvocation.RunAsync(fixture, fresh, token);
        await Assert.That(repeated.Result.CommandId).IsEqualTo(fresh.CommandId);
        await Assert.That(repeated.Result.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
        await RequestCqrsAnnEnrollmentAssertions.RequireAsync(fixture, original);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, fresh, repeated.Result, token);
        await RequestCqrsAnnTestData.ChangeAsync(fixture, token);
        var restore = original with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        before = fixture.Database.Store.Position;
        var restored = await RequestCqrsAnnInvocation.RunAsync(fixture, restore, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, restore, restored.Result, before, changed: true, token);
        await RequestCqrsAnnEnrollmentAssertions.RequireAsync(fixture, original);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, restore, restored.Result, token);
        await RequestCqrsAnnOriginalReplay.RefusePriorAsync(fixture, original, token);
    }
}

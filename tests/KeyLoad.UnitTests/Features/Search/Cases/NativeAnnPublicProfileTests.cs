namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnPublicProfileTests
{
    private const long MissingGeneration = 2;
    private const string DeniedPrincipal = "ann-profile-no-grant";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualProfileAndLegacyPublicationServeCompleteSearchAndAppliedTokenWithoutCanonicalEffects(bool configuredProfile)
        => NativeAnnPublicProfileFlow.RunAsync(configuredProfile, NativeAnnPublicProfileFlow.HealthyAsync);

    [Test]
    public Task ActualProfileDefinitionConflictAndPublicRefusalsLeaveConsumerAndHealthyPublicationIntact()
        => NativeAnnPublicProfileFlow.RunAsync(true, async (database, runtime, pin, receipt) =>
        {
            await NativeAnnPublicProfileFlow.RequireDefinitionConflictAsync(database, pin);
            AnnSeedTestSupport.Persist(database, DeniedPrincipal, Capability.None, []);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            var engine = AnnPublicWholeFlow.Engine(database, runtime);
            var request = AnnPublicWholeFlow.Request(database, pin);
            var token = TestContext.Current!.Execution.CancellationToken;
            await FailureAsync(() => engine.ApproximateSearchAsync(NativeAnnWaitWholeFlow.Root,
                request with { IndexGeneration = MissingGeneration }, token), ErrorCode.HistoryUnavailable);
            await FailureAsync(() => engine.ApproximateSearchAsync(DeniedPrincipal,
                request with { IndexGeneration = MissingGeneration }, token), ErrorCode.PermissionDenied);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await NativeAnnPublicProfileFlow.HealthyAsync(database, runtime, pin, receipt);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });

    private static async Task FailureAsync(Func<Task<AnnSearchPage>> operation, ErrorCode code)
    {
        AnnSearchPage? partial = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await operation())
            ?? throw new InvalidOperationException("The genuine public ANN refusal was absent.");
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
    }
}

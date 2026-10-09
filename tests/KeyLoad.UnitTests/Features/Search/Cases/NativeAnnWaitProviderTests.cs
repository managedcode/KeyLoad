namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnWaitProviderTests
{
    private const long MissingGeneration = 2;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualMissingGenerationOrDisabledCapabilityRejectsNoPartialThenProvisionedPrefixIsHealthy(bool disabled)
        => NativeAnnWaitWholeFlow.RunAsync(async (fixture, runtime, pin, receipt) =>
        {
            var database = fixture.Canonical;
            var request = NativeAnnWaitWholeFlow.Request(database, pin, receipt);
            var image = NativeAnnMaintenanceTestData.Snapshot(database);
            var cut = database.Store.Position;
            WaitForAnnIndexResult? partial = null;
            var engine = AnnPublicWholeFlow.Engine(database, runtime, enabled: !disabled);
            var invalid = disabled ? request : request with { IndexGeneration = MissingGeneration };
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await engine.WaitForAnnIndexAsync(NativeAnnWaitWholeFlow.Root, invalid, TestContext.Current!.Execution.CancellationToken))
                ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(disabled ? ErrorCode.UnsupportedCapability : ErrorCode.HistoryUnavailable);
            await Assert.That(partial).IsNull();
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
            await NativeAnnWaitWholeFlow.HealthyAsync(database, runtime, request, TestContext.Current!.Execution.CancellationToken);
            await AnnPublicWholeFlow.UnchangedAsync(database, image, cut);
        });
}

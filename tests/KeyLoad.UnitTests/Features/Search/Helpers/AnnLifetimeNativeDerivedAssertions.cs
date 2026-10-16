namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeDerivedAssertions
{
    internal static async Task PageAsync(AnnSearchPage page, TestDatabase target, long cut, bool cross, bool allowed, bool sourceInputSchema = true)
    {
        RankedDocument Row(int index, double score) => new(new(new(target.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Id(index)), AnnLifetimeNativeProtocol.Revision, AnnLifetimeNativeProtocol.Projected, true,
            (cross || !sourceInputSchema) ? [AnnSeedTestSupport.Field, AnnLifetimeNativeProtocol.TargetSecret]
                : [AnnSeedTestSupport.Field, AnnLifetimeNativeProtocol.InputField, AnnLifetimeNativeProtocol.TargetSecret]), score);
        RankedDocument[] rows = allowed
            ? [Row(AnnLifetimeNativeProtocol.BothOwner, AnnLifetimeNativeProtocol.FirstScore),
                Row(AnnLifetimeNativeProtocol.BothTeam, AnnLifetimeNativeProtocol.SecondScore),
                Row(AnnLifetimeNativeProtocol.BothProject, AnnLifetimeNativeProtocol.ThirdScore)]
            : [Row(AnnLifetimeNativeProtocol.BothTeam, AnnLifetimeNativeProtocol.FirstScore),
                Row(AnnLifetimeNativeProtocol.BothProject, AnnLifetimeNativeProtocol.SecondScore)];
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version, [.. rows], cut, AnnPageMode.Exact, true,
            NativeAnnMaintenanceTestData.Generation, AnnPageMode.Approximate);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

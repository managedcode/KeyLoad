namespace KeyLoad.FunctionalTests.Search;

internal static class AnnLifetimeDerivedAssertions
{
    internal static async Task PageAsync(AnnSearchPage page, TestDatabase target, long cut, bool cross, bool allowed, bool sourceInputSchema = true)
    {
        RankedDocument Row(int index, double score) => new(new(target.ExpectedReference(new(target.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Id(index))), AnnLifetimeProtocol.Revision, AnnLifetimeProtocol.Projected, true,
            (cross || !sourceInputSchema) ? [AnnSeedTestSupport.Field, AnnLifetimeProtocol.TargetSecret]
                : [AnnSeedTestSupport.Field, AnnLifetimeProtocol.InputField, AnnLifetimeProtocol.TargetSecret]), score);
        RankedDocument[] rows = allowed
            ? [Row(AnnLifetimeProtocol.BothOwner, AnnLifetimeProtocol.FirstScore),
                Row(AnnLifetimeProtocol.BothTeam, AnnLifetimeProtocol.SecondScore),
                Row(AnnLifetimeProtocol.BothProject, AnnLifetimeProtocol.ThirdScore)]
            : [Row(AnnLifetimeProtocol.BothTeam, AnnLifetimeProtocol.FirstScore),
                Row(AnnLifetimeProtocol.BothProject, AnnLifetimeProtocol.SecondScore)];
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version, [.. rows], cut, AnnPageMode.Exact, true,
            AnnMaintenanceTestData.Generation, AnnPageMode.Approximate);
        await Assert.That(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(page)))
            .IsEqualTo(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(expected)));
    }
}

using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeAssertions
{
    internal static async Task PageAsync(AnnSearchPage page, TestDatabase target, long cut, bool administrator = false)
    {
        RankedDocument Row(int index, double score) => new(new(new(target.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Id(index)), AnnLifetimeProtocol.Revision,
            administrator ? AnnLifetimeProtocol.AdminProjected : AnnLifetimeProtocol.Projected, true,
            [AnnSeedTestSupport.Field, AnnLifetimeProtocol.TargetSecret]), score);
        RankedDocument[] rows = administrator
            ? [Row(AnnLifetimeProtocol.BothOwner, AnnLifetimeProtocol.FirstScore)]
            : [Row(AnnLifetimeProtocol.BothTeam, AnnLifetimeProtocol.FirstScore),
                Row(AnnLifetimeProtocol.BothProject, AnnLifetimeProtocol.SecondScore),
                Row(AnnLifetimeProtocol.BothOwner, AnnLifetimeProtocol.ThirdScore)];
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version, [.. rows], cut, AnnPageMode.Exact, true,
            AnnMaintenanceTestData.Generation, AnnPageMode.Approximate);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task RejectedAsync(Func<Task<AnnSearchPage>> read, ErrorCode code)
    {
        AnnSearchPage? partial = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await read())
            ?? throw new InvalidOperationException(AnnLifetimeProtocol.Missing);
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
    }

    internal static async Task UnchangedAsync(TestDatabase source, (string Key, string Value)[] sourceRows, long sourceCut,
        TestDatabase target, (string Key, string Value)[] targetRows, long targetCut)
    {
        await Assert.That(AnnMaintenanceTestData.Snapshot(source)).IsEquivalentTo(sourceRows, CollectionOrdering.Matching);
        await Assert.That(source.Store.Position).IsEqualTo(sourceCut);
        await Assert.That(AnnMaintenanceTestData.Snapshot(target)).IsEquivalentTo(targetRows, CollectionOrdering.Matching);
        await Assert.That(target.Store.Position).IsEqualTo(targetCut);
    }
}

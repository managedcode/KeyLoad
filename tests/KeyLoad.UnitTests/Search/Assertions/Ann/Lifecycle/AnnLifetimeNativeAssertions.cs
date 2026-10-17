using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeAssertions
{
    internal static async Task PageAsync(AnnSearchPage page, TestDatabase target, long cut, bool administrator = false)
    {
        RankedDocument Row(int index, double score) => new(new(new(target.Partition, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Id(index)), AnnLifetimeNativeProtocol.Revision,
            administrator ? AnnLifetimeNativeProtocol.AdminProjected : AnnLifetimeNativeProtocol.Projected, true,
            [AnnSeedTestSupport.Field, AnnLifetimeNativeProtocol.TargetSecret]), score);
        RankedDocument[] rows = administrator
            ? [Row(AnnLifetimeNativeProtocol.BothOwner, AnnLifetimeNativeProtocol.FirstScore)]
            : [Row(AnnLifetimeNativeProtocol.BothTeam, AnnLifetimeNativeProtocol.FirstScore),
                Row(AnnLifetimeNativeProtocol.BothProject, AnnLifetimeNativeProtocol.SecondScore),
                Row(AnnLifetimeNativeProtocol.BothOwner, AnnLifetimeNativeProtocol.ThirdScore)];
        var expected = new AnnSearchPage(AnnPublicWholeFlow.Version, [.. rows], cut, AnnPageMode.Exact, true,
            NativeAnnMaintenanceTestData.Generation, AnnPageMode.Approximate);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task RejectedAsync(Func<Task<AnnSearchPage>> read, ErrorCode code)
    {
        AnnSearchPage? partial = null;
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial = await read())
            ?? throw new InvalidOperationException(AnnLifetimeNativeProtocol.Missing);
        await Assert.That(error.Code).IsEqualTo(code);
        await Assert.That(partial).IsNull();
    }

    internal static async Task UnchangedAsync(TestDatabase source, (string Key, string Value)[] sourceRows, long sourceCut,
        TestDatabase target, (string Key, string Value)[] targetRows, long targetCut)
    {
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(source)).IsEquivalentTo(sourceRows, CollectionOrdering.Matching);
        await Assert.That(source.Store.Position).IsEqualTo(sourceCut);
        await Assert.That(NativeAnnMaintenanceTestData.Snapshot(target)).IsEquivalentTo(targetRows, CollectionOrdering.Matching);
        await Assert.That(target.Store.Position).IsEqualTo(targetCut);
    }
}

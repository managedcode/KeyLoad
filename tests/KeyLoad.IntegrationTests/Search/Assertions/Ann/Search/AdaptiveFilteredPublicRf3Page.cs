namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Page
{
    internal static async Task RequireAsync(AdaptiveFilteredPublicRf3State state, AdaptiveFilteredPublicRf3Cohort cohort,
        AnnSearchPage actual)
    {
        var expectedRows = AdaptiveFilteredPublicRf3Recipe.Expected(state.Scenario.Partition, cohort.Eligible,
            AdaptiveFilteredPublicRf3Protocol.Limit);
        var mode = cohort.Eligible.Length <= AdaptiveFilteredPublicRf3Protocol.ExactThreshold
            ? AnnPageMode.Exact : AnnPageMode.Approximate;
        await Assert.That(actual.Position).IsGreaterThan(0L);
        var expected = new AnnSearchPage(AdaptiveFilteredPublicRf3Protocol.Version, [.. expectedRows], actual.Position,
            mode, mode == AnnPageMode.Exact, state.Pin.IndexGeneration, AnnPageMode.Approximate);
        await AdaptiveFilteredPublicRf3Equal.RequireAsync(expected, actual);
        var originalTop = expectedRows.Select(row => row.Document.Reference.Id).ToHashSet(StringComparer.Ordinal);
        var recalled = actual.Documents.Count(row => originalTop.Contains(row.Document.Reference.Id));
        var denominator = expectedRows.Length;
        if (denominator > 0)
        {
            await Assert.That((double)recalled / denominator).IsGreaterThanOrEqualTo(0.95d);
        }
    }
}

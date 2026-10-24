
namespace KeyLoad.UnitTests.QueryExecution;

internal static class DistributedSearchHybridAssertions
{
    private const double FirstDenominator = 61;
    private const double SecondDenominator = 62;
    private const double IndependentTextWeight = 2;
    private const double IndependentVectorWeight = 1;

    internal static async Task RequireAsync(QueryFixture fixture, CancellationToken token)
    {
        RankedDocument[] expected =
        [
            new(new(fixture.ExpectedDestinationReference, QueryFixture.First,
                QueryFixture.ProjectedJson, true, [QueryFixture.Secret]),
                IndependentTextWeight / FirstDenominator + IndependentVectorWeight / SecondDenominator),
            new(new(fixture.ExpectedSourceReference, QueryFixture.First,
                QueryDataset.SourceProjected, true, [QueryFixture.Secret]),
                IndependentTextWeight / SecondDenominator + IndependentVectorWeight / FirstDenominator)
        ];
        var actual = DistributedSearchHybridFlow.Execute(fixture, token);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

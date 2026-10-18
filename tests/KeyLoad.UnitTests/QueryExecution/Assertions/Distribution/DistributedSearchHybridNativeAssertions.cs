
namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchHybridNativeAssertions
{
    private const double FirstDenominator = 61;
    private const double SecondDenominator = 62;
    private const double IndependentTextWeight = 2;
    private const double IndependentVectorWeight = 1;

    internal static async Task RequireAsync(QueryNativeFixture fixture, CancellationToken token)
    {
        RankedDocument[] expected =
        [
            new(new(fixture.ExpectedDestinationReference, QueryNativeFixture.First,
                QueryNativeFixture.ProjectedJson, true, [QueryNativeFixture.Secret]),
                IndependentTextWeight / FirstDenominator + IndependentVectorWeight / SecondDenominator),
            new(new(fixture.ExpectedSourceReference, QueryNativeFixture.First,
                QueryNativePartitionFlow.SourceProjected, true, [QueryNativeFixture.Secret]),
                IndependentTextWeight / SecondDenominator + IndependentVectorWeight / FirstDenominator)
        ];
        var actual = DistributedSearchHybridNativeFlow.Execute(fixture, token);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

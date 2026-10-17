using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchHybridNativeAssertions
{
    private const double FirstDenominator = 61;
    private const double SecondDenominator = 62;
    private const double IndependentTextWeight = 2;
    private const double IndependentVectorWeight = 1;

    internal static async Task RequireAsync(RemoteDocumentNativeFixture fixture, CancellationToken token)
    {
        RankedDocument[] expected =
        [
            new(new(RemoteDocumentNativeFixture.Reference, RemoteDocumentNativeFixture.First,
                RemoteDocumentNativeFixture.ProjectedJson, true, [RemoteDocumentNativeFixture.Secret]),
                IndependentTextWeight / FirstDenominator + IndependentVectorWeight / SecondDenominator),
            new(new(new(RemotePartitionQueryNativeFlow.Local, RemoteDocumentNativeFixture.Collection,
                RemoteDocumentNativeFixture.DocumentId), RemoteDocumentNativeFixture.First,
                RemotePartitionQueryNativeFlow.SourceProjected, true, [RemoteDocumentNativeFixture.Secret]),
                IndependentTextWeight / SecondDenominator + IndependentVectorWeight / FirstDenominator)
        ];
        var actual = DistributedSearchHybridNativeFlow.Execute(fixture, token);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}

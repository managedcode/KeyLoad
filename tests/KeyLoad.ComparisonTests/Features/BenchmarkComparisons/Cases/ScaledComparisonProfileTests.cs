using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ScaledComparisonProfileTests
{
    [Test]
    public async Task ActiveProfileParserAdmitsOnlyOneHundredThousandAndOneMillion()
    {
        var oneHundredThousand = ScaledComparisonProfileParser.Parse("scaled-100k-c16");
        var oneMillion = ScaledComparisonProfileParser.Parse("scaled-1m-c16");
        await Assert.That(oneHundredThousand.Documents).IsEqualTo(100_000);
        await Assert.That(oneMillion.Documents).IsEqualTo(1_000_000);
        await Assert.That(() => ScaledComparisonProfileParser.Parse("scaled-5m-c16"))
            .Throws<ArgumentOutOfRangeException>();
    }
}

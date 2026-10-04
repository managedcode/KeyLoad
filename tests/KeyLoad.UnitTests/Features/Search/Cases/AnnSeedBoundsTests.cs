using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class AnnSeedBoundsTests
{
    private const int MaximumDimension = 4_096;
    private const int UndefinedMetricValue = int.MaxValue;
    private const int MaximumRecords = 5_000_000;
    private const long MinimumMemoryBytes = 1_024;
    private const long MaximumOwnedBytes = 8_589_934_592;
    private const long MaximumPeakBytes = 17_179_869_184;
    private const long MaximumWorkUnits = 1_000_000_000_000;
    private const string EmptyDocumentJson = "{}";

    [Test]
    public async Task MaximumDimensionCaptureHashesTheCompleteMultiBlockVector()
    {
        using var database = AnnSeedTestSupport.Create(0);
        var space = AnnSeedTestSupport.Space(dimension: MaximumDimension);
        var values = ImmutableArray.CreateRange(Enumerable.Range(0, MaximumDimension)
            .Select(index => (float)(index % 97) + 0.25f));
        database.Commit(
            new PutDocument(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), EmptyDocumentJson,
                Access: new(AnnSeedTestSupport.Owner)),
            new PutVector(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
                values, space, 1));
        var seed = AnnSeedTestSupport.Capture(database, space: space);
        var expectedRecord = new VectorRecord(AnnSeedTestSupport.Id(0), AnnSeedTestSupport.Field,
            space, values, 1);
        var expectedDigest = AnnSeedDigestOracle.Compute(AnnSeedTestSupport.Field, space, [expectedRecord]);

        await Assert.That(seed.Records).HasSingleItem();
        await Assert.That(seed.Records.Single().Values.Length).IsEqualTo(MaximumDimension);
        await Assert.That(seed.Records.Single().Values.ToArray())
            .IsEquivalentTo(values.ToArray(), CollectionOrdering.Matching);
        await Assert.That(seed.CorpusSha256).IsEqualTo(expectedDigest);
    }

    [Test]
    public async Task DimensionAndMetricBeyondRequestBoundsAreValidationErrors()
    {
        using var database = AnnSeedTestSupport.Create(0);
        var invalidSpaces = new[]
        {
            AnnSeedTestSupport.Space(dimension: MaximumDimension + 1),
            AnnSeedTestSupport.Space(metric: (DistanceMetric)UndefinedMetricValue)
        };

        foreach (var space in invalidSpaces)
        {
            var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal, space);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
    }

    [Test]
    public async Task NumericOptionValuesOutsideFrozenBoundsAreValidationErrors()
    {
        using var database = AnnSeedTestSupport.Create(0);
        AnnSeedOptions[] invalidOptions =
        [
            new() { MaxRecords = 0 },
            new() { MaxRecords = MaximumRecords + 1 },
            new() { MaxOwnedBytes = MinimumMemoryBytes - 1 },
            new() { MaxOwnedBytes = MaximumOwnedBytes + 1, MaxPeakBytes = MaximumPeakBytes },
            new() { MaxOwnedBytes = MinimumMemoryBytes, MaxPeakBytes = MinimumMemoryBytes - 1 },
            new() { MaxPeakBytes = MaximumPeakBytes + 1 },
            new() { MaxWorkUnits = 0 },
            new() { MaxWorkUnits = MaximumWorkUnits + 1 }
        ];

        foreach (var options in invalidOptions)
        {
            var failure = AnnSeedTestSupport.CaptureFailure(database, AnnSeedTestSupport.Principal,
                options: options);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
    }
}

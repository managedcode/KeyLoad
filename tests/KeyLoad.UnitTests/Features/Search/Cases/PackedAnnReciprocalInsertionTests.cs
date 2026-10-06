namespace KeyLoad.UnitTests.Features.Search;

internal sealed class PackedAnnReciprocalInsertionTests
{
    [Test]
    public Task SmallDotProductAdjacencyIsUniqueOrderedAndDeterministic()
        => PackedAnnReciprocalInsertionAssertions.AssertCorpusAsync(128, 8);

    [Test, NotInParallel]
    public Task TenThousandDotProductAdjacencyIsUniqueOrderedAndDeterministic()
        => PackedAnnReciprocalInsertionAssertions.AssertCorpusAsync(
            PackedAnnTestData.QualityRecordCount, 64);
}

namespace KeyLoad.UnitTests.Search;

internal sealed class Bm25ConfiguredQuotaTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ConfiguredRawByteCapacityPreservesRefusalReplacementAndReplay(bool oneByteUnder)
        => Bm25ConfiguredQuotaTrial.RunAsync(oneByteUnder);
}

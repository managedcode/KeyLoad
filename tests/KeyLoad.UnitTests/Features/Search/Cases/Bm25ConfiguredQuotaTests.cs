namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25ConfiguredQuotaTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ConfiguredRawByteCapacityPreservesNativeRefusalReplacementAndReplay(bool oneByteUnder)
        => Bm25ConfiguredQuotaTrial.RunAsync(oneByteUnder);
}

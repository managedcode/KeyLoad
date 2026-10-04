namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class Native5ServerPaxArchiveTests
{
    [Test]
    public async Task AcEpoch010VerifierAcceptsOnlyTheLeadingPinnedPaxComment()
    {
        var results = await Native5ServerPaxVerifierNodeProcess.ProbeAsync(Native5ServerProofFixture.Valid());
        await Assert.That(results.Select(result => result.Name)).IsEquivalentTo(ExpectedCases);
        foreach (var result in results)
        {
            await Assert.That(result.Accepted).IsFalse().Because(result.Name);
            await Assert.That(result.Preserved).IsTrue().Because(result.Name);
        }
    }

    private static readonly string[] ExpectedCases =
    [
        "wrong PAX commit comment",
        "repeated global PAX metadata",
        "non-leading global PAX metadata",
        "unknown global PAX metadata",
        "PAX path override metadata",
        "PAX size override metadata"
    ];
}
